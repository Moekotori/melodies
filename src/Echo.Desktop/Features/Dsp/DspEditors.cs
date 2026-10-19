using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    public ObservableCollection<DspToggle> Toggles { get; } = [];
    public ObservableCollection<DspChoiceField> Choices { get; } = [];
    public ObservableCollection<DspRackRow> RackRows { get; } = [];
    private readonly List<string> rackOrder = [];
    [ObservableProperty] private DspRackRow? selectedRack;
    [ObservableProperty] private string roomFacts = "";
    [ObservableProperty] private string replayFacts = "";
    [ObservableProperty] private string outputFacts = "";
    [ObservableProperty] private string irPath = "";
    private string loadedIrPath = "";
    public bool CanMoveRackUp => CanEdit && IsRack && SelectedRack is { Pinned: false } row && rackOrder.IndexOf(row.Id) > 0;
    public bool CanMoveRackDown => CanEdit && IsRack && SelectedRack is { Pinned: false } row && rackOrder.IndexOf(row.Id) is var index && index >= 0 && index < rackOrder.Count - 1;

    partial void OnSelectedRackChanged(DspRackRow? value)
    {
        OnPropertyChanged(nameof(CanMoveRackUp)); OnPropertyChanged(nameof(CanMoveRackDown));
        OpenRackSelection(value);
    }

    private void AttachOptions(JsonObject state)
    {
        foreach (var definition in SelectedModule.Toggles)
        {
            var toggle = new DspToggle(definition.Key, definition.Label,
                definition.Key == "limiterAutoCeiling" && !state.ContainsKey(definition.Key) ? state["dspLimiterCeilingDb"] is null : Bool(state, definition.Key));
            toggle.PropertyChanged += DraftChanged; Toggles.Add(toggle);
        }
        foreach (var definition in SelectedModule.Choices)
        {
            if (definition.Options.Length == 0) continue;
            var field = new DspChoiceField(definition, state[definition.Key]?.ToString() ?? "");
            field.PropertyChanged += DraftChanged; Choices.Add(field);
        }
        if (IsRack) InitializeRack(state);
    }

    private void CaptureOptions(JsonObject state)
    {
        foreach (var toggle in Toggles) state[toggle.Key] = toggle.Value;
        foreach (var field in Choices) state[field.Key] = field.Selected.Key;
        if (IsChannel) CaptureBands(state);
        if (IsRack) CaptureRack(state);
        if (IsRoom) { state["irPath"] = IrPath; state["clearIr"] = IrPath.Length == 0 && HasImpulse(state); }
    }

    private void CaptureRack(JsonObject state)
    {
        if (rackWorkspace is not null)
        { state["filters"] = rackWorkspace["filters"]?.DeepClone(); state["procedural"] = rackWorkspace["procedural"]?.DeepClone(); }
        DspRackDraft.Reorder(state, rackOrder);
    }

    private void CaptureBands(JsonObject state)
    {
        var bands = new JsonObject();
        foreach (var id in new[] { "low", "mid", "high" })
            bands[id] = new JsonObject { ["leftGainDb"] = Parameter($"{id}.left"), ["rightGainDb"] = Parameter($"{id}.right") };
        state["bandGains"] = bands;
        foreach (var id in new[] { "low", "mid", "high" }) { state.Remove($"{id}.left"); state.Remove($"{id}.right"); }
    }

    private double Parameter(string key) => Parameters.FirstOrDefault(x => x.Definition.Key == key)?.Value ?? 0;

    private double InitialParameter(JsonObject state, DspParameterDefinition definition)
    {
        if (definition.Key == "dspLimiterCeilingDb" && state[definition.Key] is null) return -1;
        if (IsChannel && definition.Key.Split('.') is [var band, var side] && state["bandGains"] is JsonObject bands && bands[band] is JsonObject pair)
            return Number(pair, side == "left" ? "leftGainDb" : "rightGainDb");
        return DspParameterPath.Read(state, definition.Key) is JsonValue value && value.TryGetValue<double>(out var number)
            ? number : 0;
    }

    private void DescribeRoom(JsonObject state)
    {
        var name = state["irName"]?.ToString();
        var error = state["error"]?.ToString();
        RoomFacts = string.IsNullOrWhiteSpace(name) ? "还没有导入脉冲响应。"
            : $"{name} · {Number(state, "sampleRate"):0} Hz · {Number(state, "tapCount"):0} 点 · 延迟 {Number(state, "latencySamples"):0} 采样"
            + (string.IsNullOrWhiteSpace(error) ? "" : " · " + error);
        if (string.IsNullOrWhiteSpace(IrPath) && state["irPath"]?.ToString() is { Length: > 0 } path) IrPath = path;
    }

    private static string DescribeReplay(JsonObject state)
        => $"曲目 {Number(state, "trackGainDb"):+0.0;-0.0;0.0} dB · 专辑 {Number(state, "albumGainDb"):+0.0;-0.0;0.0} dB · 正在应用 {Number(state, "appliedGainDb"):+0.0;-0.0;0.0} dB";

    private static bool HasImpulse(JsonObject state) => state["irName"]?.ToString() is { Length: > 0 } || state["irId"]?.ToString() is { Length: > 0 };

    private static string RackLabel(string id) => id switch
    {
        "equalizer" => "参数 EQ", "convolution" => "房间校正", "replayGain" => "音量均衡", "compressor" => "压缩器",
        "crossfeed" => "交叉馈送", "stereoField" => "立体声场", "channelMatrix" => "声道矩阵", "channelBalance" => "声道工具",
        "multiband" => "多段压缩", "deesser" => "齿音抑制", "procedural" => "运算处理",
        "headroom" => "Headroom", "truePeakLimiter" => "安全限幅", "playbackRate" => "播放速率", "levelMeter" => "电平表", _ => id,
    };

    private void ReleaseOptions()
    {
        foreach (var toggle in Toggles) toggle.PropertyChanged -= DraftChanged;
        foreach (var field in Choices) field.PropertyChanged -= DraftChanged;
        Toggles.Clear(); Choices.Clear(); ReleaseRack(); RackRows.Clear(); rackOrder.Clear(); SelectedRack = null;
    }

    [RelayCommand] private void MoveRackUp() => MoveRack(-1);
    [RelayCommand] private void MoveRackDown() => MoveRack(1);

    private void MoveRack(int delta)
    {
        if (SelectedRack is not { Pinned: false } row) return;
        var index = rackOrder.IndexOf(row.Id);
        var next = index + delta;
        if (index < 0 || next < 0 || next >= rackOrder.Count) return;
        (rackOrder[index], rackOrder[next]) = (rackOrder[next], rackOrder[index]);
        var pinned = RackRows.Where(x => x.Pinned).ToArray();
        RebuildRack();
        foreach (var stage in pinned) { RackRows.Add(stage); RackStages.Add("固定 · " + stage.Id); }
        SelectedRack = RackRows.FirstOrDefault(x => x.Id == rackOrder[next]);
        IsDirty = true; NoteEdit();
        OnPropertyChanged(nameof(CanMoveRackUp)); OnPropertyChanged(nameof(CanMoveRackDown));
    }

    [RelayCommand] private void ResetChannel()
    {
        if (!CanEdit || !IsChannel) return;
        projecting = true;
        try
        {
            foreach (var parameter in Parameters) parameter.Value = 0;
            foreach (var toggle in Toggles) toggle.Value = false;
            foreach (var field in Choices) field.Selected = field.Options[0];
        }
        finally { projecting = false; }
        IsDirty = true; NoteEdit();
    }

    [RelayCommand] private void SwapChannels()
    {
        if (!CanEdit || !IsChannel) return;
        projecting = true;
        try
        {
            Swap("leftGainDb", "rightGainDb"); Swap("leftDelayMs", "rightDelayMs");
            Swap("low.left", "low.right"); Swap("mid.left", "mid.right"); Swap("high.left", "high.right");
        }
        finally { projecting = false; }
        IsDirty = true; NoteEdit();
    }

    private void Swap(string left, string right)
    {
        var a = Parameters.FirstOrDefault(x => x.Definition.Key == left);
        var b = Parameters.FirstOrDefault(x => x.Definition.Key == right);
        if (a is null || b is null) return;
        (a.Value, b.Value) = (b.Value, a.Value);
    }

    public void UseImpulse(string path)
    {
        if (!CanEdit || !IsRoom || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { Message = "找不到这个脉冲文件。"; return; }
        IrPath = path; RoomFacts = "准备导入 " + Path.GetFileName(path);
        IsDirty = true; NoteEdit();
    }

    [RelayCommand] private void ClearImpulse()
    {
        if (!CanEdit || !IsRoom) return;
        IrPath = ""; loadedIrPath = ""; RoomFacts = "还没有导入脉冲响应。";
        IsDirty = true; NoteEdit();
    }
}
