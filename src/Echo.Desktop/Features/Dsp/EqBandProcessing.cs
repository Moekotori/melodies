using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class EqBandViewModel
{
    public IReadOnlyList<DspChoice> Scopes { get; } = [new("stereo", "立体声"), new("left", "左声道"), new("right", "右声道"), new("mid", "中置"), new("side", "侧向")];
    public IReadOnlyList<DspChoice> DynamicModes { get; } = [new("off", "关闭"), new("down", "向下压缩"), new("up", "向上扩展")];
    [ObservableProperty] private DspChoice scope = new("stereo", "立体声");
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DynamicActive))] private DspChoice dynamicMode = new("off", "关闭");
    [ObservableProperty] private double dynamicThresholdDb = -24;
    [ObservableProperty] private double dynamicRatio = 2;
    [ObservableProperty] private double dynamicAttackMs = 5;
    [ObservableProperty] private double dynamicReleaseMs = 80;
    public bool SupportsDynamic => !IsCorrection && Filter.Key == "peaking";
    public bool DynamicActive => SupportsDynamic && DynamicMode.Key != "off";

    internal void ReadProcessing(JsonObject state)
    {
        Scope = Scopes.FirstOrDefault(x => x.Key == (state["scope"]?.ToString() ?? "stereo")) ?? Scopes[0];
        DynamicMode = DynamicModes.FirstOrDefault(x => x.Key == (state["dynamicMode"]?.ToString() ?? "off")) ?? DynamicModes[0];
        DynamicThresholdDb = Number("dynamicThresholdDb", -24); DynamicRatio = Number("dynamicRatio", 2);
        DynamicAttackMs = Number("dynamicAttackMs", 5); DynamicReleaseMs = Number("dynamicReleaseMs", 80);
        double Number(string key, double fallback) => state[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : fallback;
    }

    internal JsonObject ToState() => new()
    {
        ["frequencyHz"] = FrequencyHz, ["gainDb"] = GainDb, ["q"] = Q, ["filterType"] = Filter.Key, ["enabled"] = Enabled,
        ["scope"] = Scope.Key, ["dynamicMode"] = DynamicMode.Key, ["dynamicThresholdDb"] = DynamicThresholdDb,
        ["dynamicRatio"] = DynamicRatio, ["dynamicAttackMs"] = DynamicAttackMs, ["dynamicReleaseMs"] = DynamicReleaseMs
    };
}
