using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Playback;

namespace Echo.Desktop.Features.Dsp;

public sealed record DspMetric(string Label, string Value);

public sealed partial class DspViewModel
{
    [ObservableProperty] private IReadOnlyList<DspMetric> runtimeMetrics = [];
    [ObservableProperty] private string runtimeHeadline = "等待宿主运行数据";
    [ObservableProperty] private string gainReductionReadout = "—";
    [ObservableProperty] private string peakReadout = "—";
    [ObservableProperty] private double peakMeterPercent;
    [ObservableProperty] private string appliedGainReadout = "—";
    [ObservableProperty] private string roomResourceReadout = "尚未载入脉冲响应";

    private void UpdateWorkbenchFacts()
    {
        var snapshot = session?.Snapshot;
        var running = snapshot?.Status == PlaybackStatus.Playing;
        var processing = snapshot?.Processing;
        var stage = Child(processing, SelectedModule.Id switch { "src" => "echoSrc", "sdm" => "sdm", _ => "limiter" });
        var metrics = new List<DspMetric>(8);
        RuntimeHeadline = stage is null ? "等待宿主运行数据" : Flag(stage, "active") ? "宿主已确认处理激活" : "宿主当前未激活处理";
        GainReductionReadout = IsOutput ? "—" : ReadMetric(stage, "gainReductionDb", "0.0");
        var level = running ? session?.State.Level : null;
        PeakReadout = level is null ? "—" : $"{level.PeakDb:0.0}";
        PeakMeterPercent = level is null ? 0 : Math.Clamp((level.PeakDb + 60) / 60 * 100, 0, 100);
        if (IsOutput)
        {
            Add("输入", "sourceSampleRate", "0", " Hz"); Add("输出", "targetSampleRate", "0", " Hz");
            Add("FIR 级数", "stageCount", "0");
            metrics.Add(new("实际计算后端", TextOf(stage, "activeBackend")?.ToUpperInvariant() ?? "—"));
            Add("平均处理", "averageProcessMilliseconds", "0.###", " ms"); Add("峰值处理", "peakProcessMilliseconds", "0.###", " ms");
            Add("标称延迟", "nominalLatencyMilliseconds", "0.###", " ms");
            if (IsSdm) Add("实时预算比", "realtimeAdmissionRatio", "0.###");
        }
        else
        {
            metrics.Add(new("输出峰值", PeakReadout == "—" ? "—" : PeakReadout + " dBFS"));
            metrics.Add(new("安全限幅", stage is null ? "未确认" : Flag(stage, "protecting") ? "正在保护" : Flag(stage, "active") ? "已激活" : "未激活"));
            Add("峰值上限", "ceilingDb", "0.0", " dBTP"); Add("当前限幅衰减", "gainReductionDb", "0.0", " dB");
        }
        if (IsReplayGain && confirmed is not null)
        {
            AppliedGainReadout = confirmed["appliedGainDb"] is null ? "—" : $"{Number(confirmed, "appliedGainDb"):+0.0;-0.0;0.0}";
            metrics = [new("曲目增益", $"{Number(confirmed, "trackGainDb"):+0.0;-0.0;0.0} dB"), new("专辑增益", $"{Number(confirmed, "albumGainDb"):+0.0;-0.0;0.0} dB"), new("当前模式", ReplayMode.Label), new("防止削波", PreventClipping ? "开启" : "关闭")];
        }
        if (IsRoom && confirmed is not null)
        {
            RoomResourceReadout = confirmed["irName"]?.ToString() is { Length: > 0 } name ? name : "尚未载入脉冲响应";
            metrics = [new("采样率", $"{Number(confirmed, "sampleRate"):0} Hz"), new("卷积长度", $"{Number(confirmed, "tapCount"):0} taps"), new("延迟", $"{Number(confirmed, "latencySamples"):0} samples"), new("声道模式", confirmed["channelMode"]?.ToString() ?? "—")];
            RuntimeHeadline = RoomFacts;
            OnPropertyChanged(nameof(HasRoomImpulse));
        }
        if (SelectedModule.Id is "compressor" or "multiband" or "deesser")
        {
            // The current typed snapshot does not carry these meters. Never substitute limiter data.
            GainReductionReadout = "—"; RuntimeHeadline = "动态增益测量尚未由当前连接提供";
            metrics = [new("当前模块", SelectedModule.Title), new("配置确认", SelectedModule.Status), new("实时增益降低", "—"), new("输出峰值", PeakReadout == "—" ? "—" : PeakReadout + " dBFS")];
        }
        RuntimeMetrics = metrics;
        OnPropertyChanged(nameof(ConversionOutputRate));
        void Add(string label, string key, string format, string unit = "")
        { var value = ReadMetric(stage, key, format); metrics.Add(new(label, value == "—" ? value : value + unit)); }
    }
    private static string ReadMetric(JsonElement? state, string key, string format)
        => state is { ValueKind: JsonValueKind.Object } actual && actual.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number.ToString(format) : "—";
}
