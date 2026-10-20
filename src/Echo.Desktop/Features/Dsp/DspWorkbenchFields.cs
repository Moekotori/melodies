using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    public IReadOnlyDictionary<string, DspParameter> Fields { get; private set; } = new Dictionary<string, DspParameter>();
    public IReadOnlyDictionary<string, DspChoiceField> Options { get; private set; } = new Dictionary<string, DspChoiceField>();
    public IReadOnlyDictionary<string, DspToggle> Switches { get; private set; } = new Dictionary<string, DspToggle>();
    public IReadOnlyDictionary<string, DspParameter> BandFields { get; private set; } = new Dictionary<string, DspParameter>();
    public IReadOnlyDictionary<string, DspToggle> BandSwitches { get; private set; } = new Dictionary<string, DspToggle>();
    [ObservableProperty] private int workbenchRevision;
    public string CurrentModuleId => SelectedModule.Id;
    public bool CrossfeedUsesBs2b => Options.GetValueOrDefault("model")?.Selected.Key == "bs2b";
    // The parent section owns busy/ready disabling; this condition only follows auto/manual mode.
    public bool CanEditLimiterCeiling => Switches.GetValueOrDefault("limiterAutoCeiling")?.Value == false;
    public string HeadroomReadout => $"{Parameter("headroomDb"):+0.0;-0.0;0.0}";
    public string SpatialReadout => SelectedModule.Id switch
    {
        "crossfeed" => $"{Parameter("amount"):0%} · {Parameter("cutoffHz"):0} Hz",
        "virtualStage" => $"±{Parameter("azimuthDegrees"):0}° · 反射 {Parameter("reflection"):0%}",
        "stereoField" => $"{Parameter("width"):0.00}× · 参数声场",
        "channelMatrix" => $"左路 {Math.Abs(Parameter("leftToLeft")) + Math.Abs(Parameter("rightToLeft")):0.00}× · 右路 {Math.Abs(Parameter("leftToRight")) + Math.Abs(Parameter("rightToRight")):0.00}×",
        _ => "参数模型"
    };
    public string InputRateReadout => session?.Snapshot is { Current: not null, SampleRate: > 0 } snapshot ? $"{snapshot.SampleRate / 1000d:0.###} kHz" : "—";
    public string RequestedRateReadout => IsSdm ? Options.GetValueOrDefault("carrierRate")?.Selected.Label ?? "—"
        : Options.GetValueOrDefault("ratio")?.Selected.Label ?? "—";
    public string FilterReadout => Options.GetValueOrDefault("filter1x")?.Selected.Label ?? "—";
    public string FilterPlanReadout => $"1×  {Options.GetValueOrDefault("filter1x")?.Selected.Label}\nNx  {Options.GetValueOrDefault("filterNx")?.Selected.Label}";
    public string OutputPlanReadout => IsSdm ? "输入 PCM → FIR 插值 → SDM 调制 → DSD 传输" : "输入 PCM → FIR 插值 → 抖动／量化 → 输出设备";

    private void BindWorkbenchFields()
    {
        Fields = Parameters.ToDictionary(x => x.Definition.Key, StringComparer.Ordinal);
        Options = Choices.ToDictionary(x => x.Key, StringComparer.Ordinal);
        Switches = Toggles.ToDictionary(x => x.Key, StringComparer.Ordinal);
        OnPropertyChanged(nameof(Fields)); OnPropertyChanged(nameof(Options)); OnPropertyChanged(nameof(Switches));
        BindBandFields(); NotifyWorkbench(); UpdateWorkbenchFacts();
    }
    private void BindBandFields()
    {
        var prefix = "bands." + CompressionBand.Key + ".";
        BandFields = Parameters.Where(x => x.Definition.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(x => x.Definition.Key[prefix.Length..], StringComparer.Ordinal);
        OnPropertyChanged(nameof(BandFields));
        BandSwitches = Toggles.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(x => x.Key[prefix.Length..], StringComparer.Ordinal);
        OnPropertyChanged(nameof(BandSwitches));
    }
    private void NotifyWorkbench()
    {
        WorkbenchRevision++;
        NotifyWorkbenchReadouts();
        OnPropertyChanged(nameof(CrossfeedUsesBs2b)); OnPropertyChanged(nameof(CanEditLimiterCeiling));
        foreach (var property in new[] { nameof(HeadroomReadout), nameof(SpatialReadout), nameof(InputRateReadout), nameof(RequestedRateReadout), nameof(FilterReadout), nameof(FilterPlanReadout), nameof(OutputPlanReadout) }) OnPropertyChanged(property);
    }
}
