using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Playback;

namespace Echo.Desktop.Features.Dsp;

// Page drafts only. All native requests use the existing playback owner's bounded command lane.
public sealed partial class DspViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackSessionCoordinator? session;
    private readonly CancellationTokenSource lifetime = new();
    private JsonObject? confirmed;
    private bool projecting, disposed;
    private int generation;
    private readonly Stack<JsonObject> undo = new();
    public IReadOnlyList<DspModule> Modules { get; } = DspModuleCatalog.Create();
    public ObservableCollection<EqBandViewModel> Bands { get; } = [];
    public ObservableCollection<DspParameter> Parameters { get; } = [];
    public ObservableCollection<string> RackStages { get; } = [];
    public IReadOnlyList<DspChoice> ReplayModes { get; } = [new("0", "关闭"), new("1", "歌曲增益"), new("2", "专辑增益")];
    [ObservableProperty] private DspModule selectedModule;
    [ObservableProperty] private EqBandViewModel? selectedBand;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isReady;
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private bool preventClipping;
    [ObservableProperty] private DspChoice replayMode;
    [ObservableProperty] private double preampDb;
    [ObservableProperty] private double strengthPercent = 100;
    [ObservableProperty] private double tiltDb;
    [ObservableProperty] private string message = "正在读取原生状态…";
    [ObservableProperty] private string confirmedStatus = "未读取";
    [ObservableProperty] private string presetName = "";
    public string OutputName { get; }
    public string OutputRate { get; }
    public int SampleRate { get; }
    public bool IsEq => SelectedModule.Id == "eq";
    public bool IsReplayGain => SelectedModule.Id == "replayGain";
    public bool IsRack => SelectedModule.Id == "rack";
    public bool IsPending => SelectedModule.ReadMethod is null;
    public bool HasEnable => SelectedModule.WriteMethod is not null && SelectedModule.Id is not ("headroom" or "replayGain");
    public bool CanEdit => IsReady && !IsBusy;
    public bool CanApply => CanEdit && IsDirty && SelectedModule.WriteMethod is not null;
    public bool CanUndo => CanEdit && undo.Count > 0;
    public string EnableLabel => SelectedModule.Id == "safety" ? "安全限幅" : "启用模块";
    public string DraftLabel => IsDirty ? "有未应用的调整" : "与原生状态一致";
    public string PendingDescription => SelectedModule.Id switch {
        "src" or "sdm" => "输出格式与设备联动正在迁移。可从音频设置选择已支持的输出格式。",
        "headphone" => "耳机型号、目标曲线与校正资料导入正在迁移。当前可使用参数均衡器手动调整。",
        "room" => "脉冲响应选择、导入与卷积状态正在迁移。",
        _ => "聆听方案的保存、切换与输出绑定正在迁移。" };
    public IRelayCommand OpenOutputCommand { get; }
    public DspViewModel(PlaybackSessionCoordinator? session, string outputName, IRelayCommand openOutput)
    {
        this.session = session; OutputName = outputName; OpenOutputCommand = openOutput;
        SampleRate = session?.Snapshot.SampleRate is > 0 and var rate ? rate : 48000;
        var outputRate = session?.Snapshot.Output is { ValueKind: JsonValueKind.Object } output
            && output.TryGetProperty("sampleRate", out var outputValue) && outputValue.TryGetInt32(out var actualRate) ? actualRate : 0;
        OutputRate = outputRate > 0 ? $"{outputRate / 1000d:0.#} kHz" : "输出采样率未确认";
        selectedModule = Modules.First(x => x.Id == "eq"); replayMode = ReplayModes[0];
        PropertyChanged += Changed;
    }
    partial void OnSelectedModuleChanged(DspModule value)
    {
        foreach (var name in new[] { nameof(IsEq), nameof(IsReplayGain), nameof(IsRack), nameof(IsPending), nameof(HasEnable), nameof(EnableLabel), nameof(PendingDescription) }) OnPropertyChanged(name);
        undo.Clear(); _ = RefreshAsync();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (!projecting && args.PropertyName is nameof(Enabled) or nameof(PreampDb) or nameof(StrengthPercent) or nameof(TiltDb) or nameof(ReplayMode) or nameof(PreventClipping)) IsDirty = true;
        if (args.PropertyName is nameof(IsReady) or nameof(IsBusy) or nameof(IsDirty))
        { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(DraftLabel)); }
    }
    private void DraftChanged(object? sender, PropertyChangedEventArgs args) { if (!projecting) IsDirty = true; }

    [RelayCommand] private async Task RefreshAsync()
    {
        var version = ++generation; var module = SelectedModule;
        IsBusy = true; IsReady = false; IsDirty = false; confirmed = null; ConfirmedStatus = "未读取";
        ClearEditors(); Message = module.ReadMethod is null ? "" : "正在读取原生状态…";
        try
        {
            if (module.ReadMethod is null) { module.Status = "待迁移"; return; }
            if (session is null) throw new IOException("播放服务尚未连接。");
            var result = await session.DspAsync(module.ReadMethod, Json(new JsonArray()), token: lifetime.Token);
            if (disposed || version != generation) return;
            if (result.ValueKind != JsonValueKind.Object) throw new IOException("原生模块没有返回有效状态。");
            confirmed = JsonNode.Parse(result.GetRawText())!.AsObject(); Project(confirmed); IsReady = true;
            Message = "";
            if (module.Id == "eq") await ReadPresetsAsync(version);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { if (!disposed && version == generation) { Message = e.Message; module.Status = "读取失败"; } }
        finally { if (!disposed && version == generation) IsBusy = false; }
    }

    [RelayCommand] private async Task ApplyAsync()
    {
        if (!CanApply || session is null || confirmed is null) return;
        var module = SelectedModule; var before = (JsonObject)confirmed.DeepClone(); var draft = CaptureDraft();
        var version = generation; IsBusy = true; Message = "正在应用…";
        try
        {
            JsonArray parameters = module.Id switch {
                "headroom" => new(draft["headroomDb"]!.DeepClone()),
                "safety" => new(JsonValue.Create(Enabled)), _ => new(draft) };
            var result = await session.DspAsync(module.WriteMethod!, Json(parameters), retainForRecovery: true, token: lifetime.Token);
            if (disposed || version != generation) return;
            if (result.ValueKind != JsonValueKind.Object) throw new IOException("原生模块没有确认调整结果。");
            confirmed = JsonNode.Parse(result.GetRawText())!.AsObject();
            if (undo.Count >= 16) undo.Clear(); undo.Push(before);
            Project(confirmed); Message = "已应用到原生处理链";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { if (!disposed && version == generation) Message = "应用失败：" + e.Message; }
        finally { if (!disposed && version == generation) { IsBusy = false; OnPropertyChanged(nameof(CanUndo)); } }
    }

    [RelayCommand] private void Revert() { if (confirmed is not null && CanEdit) Project(confirmed); }
    [RelayCommand] private void Undo()
    { if (CanUndo) { var previous = undo.Pop(); Project(previous, updateConfirmed: false); IsDirty = true; OnPropertyChanged(nameof(CanUndo)); } }
    [RelayCommand] private void Flatten()
    {
        if (!CanEdit || !IsEq) return;
        foreach (var band in Bands) { band.GainDb = 0; band.Q = 1.414; band.Filter = EqFilters.All[0]; band.Enabled = true; }
        PreampDb = 0; TiltDb = 0; StrengthPercent = 100; IsDirty = true;
    }
    [RelayCommand] private void AutoPreamp()
    { if (CanEdit && IsEq) PreampDb = Math.Clamp(-EqResponse.Maximum(Bands, SampleRate, StrengthPercent, TiltDb), -12, 0); }

    private void Project(JsonObject state, bool updateConfirmed = true)
    {
        var selectedIndex = SelectedBand?.Index;
        projecting = true;
        try
        {
            ClearEditors(); Enabled = Bool(state, SelectedModule.Id == "safety" ? "safetyLimiterEnabled" : "enabled");
            PreampDb = Math.Round(Number(state, "preampDb"), 1); StrengthPercent = Math.Round(Number(state, "strengthPercent", 100)); TiltDb = Math.Round(Number(state, "tiltDb"), 1);
            PresetName = state["presetName"]?.ToString() ?? "自定义";
            PreventClipping = Bool(state, "preventClipping"); ReplayMode = ReplayModes.FirstOrDefault(x => x.Key == state["mode"]?.ToString()) ?? ReplayModes[0];
            if (IsEq && state["bands"] is JsonArray bands)
            {
                if (bands.Count is < 1 or > 64) throw new IOException("均衡器频段数量超出界面边界。");
                for (var i = 0; i < bands.Count; i++)
                {
                    if (bands[i] is not JsonObject b) throw new IOException("无效均衡器频段。");
                    var band = new EqBandViewModel(i, Math.Round(Number(b, "frequencyHz", 1000), 3), Math.Round(Number(b, "gainDb"), 3), Math.Round(Number(b, "q", 1.414), 3), b["filterType"]?.ToString() ?? "peaking", b["enabled"] is null || Bool(b, "enabled"));
                    band.PropertyChanged += DraftChanged; Bands.Add(band);
                }
                SelectedBand = Bands.FirstOrDefault(x => x.Index == selectedIndex)
                    ?? Bands.FirstOrDefault(x => x.Enabled && Math.Abs(x.GainDb) > 0.01) ?? Bands[Bands.Count / 2];
            }
            foreach (var definition in SelectedModule.Parameters)
            { var parameter = new DspParameter(definition, Number(state, definition.Key)); parameter.PropertyChanged += DraftChanged; Parameters.Add(parameter); }
            if (IsRack)
            {
                if (state["order"] is JsonArray order) foreach (var id in order.Take(16)) RackStages.Add(id?.ToString() ?? "");
                if (state["fixedPostStages"] is JsonArray fixedStages) foreach (var id in fixedStages.Take(8)) RackStages.Add("固定 · " + id);
            }
            if (updateConfirmed)
            {
                ConfirmedStatus = IsRack ? "原生处理顺序" : IsReplayGain ? ReplayMode.Label : SelectedModule.Id == "headroom" ? $"{Number(state, "headroomDb"):+0.0;-0.0;0.0} dB"
                    : Enabled ? "已启用" : "旁路";
                SelectedModule.Status = ConfirmedStatus;
            }
            IsDirty = false;
        }
        finally { projecting = false; }
    }
    private JsonObject CaptureDraft()
    {
        var state = (JsonObject)confirmed!.DeepClone();
        if (HasEnable) state[SelectedModule.Id == "safety" ? "safetyLimiterEnabled" : "enabled"] = Enabled;
        foreach (var parameter in Parameters) state[parameter.Definition.Key] = parameter.Value;
        if (IsReplayGain) { state["mode"] = int.Parse(ReplayMode.Key); state["preventClipping"] = PreventClipping; }
        if (IsEq)
        {
            state["preampDb"] = PreampDb; state["strengthPercent"] = StrengthPercent; state["tiltDb"] = TiltDb;
            state["presetId"] = "custom"; state["presetName"] = "自定义";
            state["bands"] = new JsonArray(Bands.Select(b => (JsonNode)new JsonObject {
                ["frequencyHz"] = b.FrequencyHz, ["gainDb"] = b.GainDb, ["q"] = b.Q, ["filterType"] = b.Filter.Key, ["enabled"] = b.Enabled }).ToArray());
        }
        return state;
    }
    private void ClearEditors()
    {
        foreach (var band in Bands) band.PropertyChanged -= DraftChanged;
        foreach (var parameter in Parameters) parameter.PropertyChanged -= DraftChanged;
        Bands.Clear(); Parameters.Clear(); RackStages.Clear(); SelectedBand = null;
    }
    private static double Number(JsonObject state, string key, double fallback = 0)
        => state[key] is JsonValue value && value.TryGetValue<double>(out var n) && double.IsFinite(n) ? n : fallback;
    private static bool Bool(JsonObject state, string key) => state[key] is JsonValue value && value.TryGetValue<bool>(out var b) && b;
    private static JsonElement Json(JsonNode node) { using var doc = JsonDocument.Parse(node.ToJsonString()); return doc.RootElement.Clone(); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; generation++; lifetime.Cancel();
        PropertyChanged -= Changed; ClearEditors(); undo.Clear(); confirmed = null; lifetime.Dispose();
    }
}
