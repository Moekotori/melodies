namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    public bool HasRoomImpulse => IsRoom && confirmed is { } state
        && (Number(state, "tapCount") > 0 || state["irName"]?.ToString() is { Length: > 0 });
    public string HeadroomAmplitude => $"×{Math.Pow(10, Parameter("headroomDb") / 20):0.000}   ·   {Math.Pow(10, Parameter("headroomDb") / 20) * 100:0.0}% amplitude";
    public string ConversionOutputRate
    {
        get
        {
            var stage = Child(session?.Snapshot.Processing, IsSdm ? "sdm" : "echoSrc");
            var rate = ReadMetric(stage, "targetSampleRate", "0");
            return double.TryParse(rate, out var hz) && hz > 0 ? $"{hz / 1000:0.###} kHz" : "—";
        }
    }
    public string ConversionPrecision => Options.GetValueOrDefault("ditherBits")?.Selected.Label ?? "—";
    public IReadOnlyList<DspMetric> PlannedStages => IsSdm
        ? [new("01 · 输出格式", ChoiceLabel("outputFormat")), new("02 · FIR", ChoiceLabel("firTier")), new("03 · 调制器", ChoiceLabel("qualityProfile")), new("04 · 计算", ChoiceLabel("computeBackend"))]
        : [new("01 · 模式", ChoiceLabel("ratio")), new("02 · 输入滤波器", ChoiceLabel("filter1x")), new("03 · 高倍率滤波器", ChoiceLabel("filterNx")), new("04 · 计算", ChoiceLabel("computeBackend"))];
    public IReadOnlyList<DspMetric> SpatialMetrics
    {
        get
        {
            var mid = Math.Pow(10, Parameter("centerGainDb") / 20);
            var side = Parameter("width") * Math.Pow(10, Parameter("sideGainDb") / 20);
            return CurrentModuleId switch
            {
                "crossfeed" => [new("处理模型", ChoiceLabel("model")), new("截止频率", $"{Parameter("cutoffHz"):0} Hz"), new("馈送", CrossfeedUsesBs2b ? $"{Parameter("feedDb"):0.0} dB" : $"{Parameter("amount"):0%}")],
                "virtualStage" => [new("空间强度", $"{Parameter("strength"):0%}"), new("声源方位", $"±{Parameter("azimuthDegrees"):0}°"), new("早期反射", $"{Parameter("reflection"):0%}")],
                "stereoField" => [new("中置信号", $"{Parameter("centerGainDb"):0.0} dB"), new("侧向信号", side <= 0 ? "−∞ dB" : $"{20 * Math.Log10(side):0.0} dB"), new("参数相关系数", $"{(mid * mid - side * side) / Math.Max(1e-9, mid * mid + side * side):0.00}")],
                _ => [new("左路总量", $"{Math.Abs(Parameter("leftToLeft")) + Math.Abs(Parameter("rightToLeft")):0.00}×"), new("右路总量", $"{Math.Abs(Parameter("leftToRight")) + Math.Abs(Parameter("rightToRight")):0.00}×"), new("声道", "Stereo")]
            };
        }
    }
    private string ChoiceLabel(string key) => Options.GetValueOrDefault(key)?.Selected.Label ?? "—";
    private void NotifyWorkbenchReadouts()
    {
        OnPropertyChanged(nameof(HeadroomAmplitude)); OnPropertyChanged(nameof(ConversionOutputRate));
        OnPropertyChanged(nameof(ConversionPrecision)); OnPropertyChanged(nameof(PlannedStages)); OnPropertyChanged(nameof(SpatialMetrics));
    }
}
