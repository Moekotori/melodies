using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspModule : ObservableObject
{
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Group { get; }
    public bool HasGroupHeading => Id is "replayGain" or "src" or "eq" or "room" or "compressor";
    public string? ReadMethod { get; }
    public string? WriteMethod { get; }
    public IReadOnlyList<DspParameterDefinition> Parameters { get; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsActive))] private string status = "未读取";
    public bool IsActive => Status == "已启用";
    public string Icon => Id switch { "listening" or "headphone" => "headphones", "rack" => "queue", "replayGain" or "compressor" => "music-stat", "headroom" or "eq" => "sliders", "src" or "sdm" or "stereoField" => "waves", "room" => "monitor", "safety" => "lock", "crossfeed" => "link", _ => "shuffle" };
    public DspModule(string id, string title, string description, string group, string? read = null,
        string? write = null, params DspParameterDefinition[] parameters)
    { Id = id; Title = title; Description = description; Group = group; ReadMethod = read; WriteMethod = write; Parameters = parameters; Status = read is null ? "待迁移" : "未读取"; }
}

public sealed record DspParameterDefinition(string Key, string Label, double Minimum, double Maximum, double Step, string Unit);
public sealed record DspChoice(string Key, string Label);

internal static class DspModuleCatalog
{
    public static IReadOnlyList<DspModule> Create() => [
        new("listening", "聆听方案", "按聆听场景组织音效与输出方案。", "总览"),
        new("rack", "DSP 机架", "查看原生处理顺序及固定输出保护阶段。", "总览", "dspRack.getState"),
        new("replayGain", "ReplayGain", "依据歌曲或专辑增益标签统一播放响度。", "电平", "replayGain.getConfig", "replayGain.setConfig",
            new DspParameterDefinition("preampDb", "额外增益", -12, 12, 0.1, "dB")),
        new("headroom", "增益与余量", "为处理链预留峰值空间，减少过载。", "电平", "dsp.getState", "dsp.setHeadroom",
            new DspParameterDefinition("headroomDb", "处理链增益", -12, 12, 0.1, "dB")),
        new("src", "ECHO SRC", "采样率转换与滤波器配置。", "转换"),
        new("sdm", "ECHO SDM", "PCM → SDM 调制与 DSD 输出。", "转换"),
        new("eq", "参数均衡器", "调整频率、增益与 Q 值，塑造你的声音。", "音色", "eq.getState", "eq.setState"),
        new("headphone", "耳机校正", "匹配耳机频响与目标曲线。", "音色"),
        new("room", "房间校正", "使用脉冲响应进行卷积校正。", "空间"),
        new("channel", "声道控制", "左右增益、平衡与延时。", "空间", "channelBalance.getState", "channelBalance.setState",
            new("balance", "左右平衡", -1, 1, 0.01, ""), new("leftGainDb", "左声道增益", -12, 6, 0.1, "dB"),
            new("rightGainDb", "右声道增益", -12, 6, 0.1, "dB"), new("leftDelayMs", "左声道延时", 0, 10, 0.1, "ms"),
            new("rightDelayMs", "右声道延时", 0, 10, 0.1, "ms")),
        new("safety", "输出保护", "原生安全限幅与削波风险。", "空间", "dsp.getState", "dsp.setSafetyLimiter"),
        new("compressor", "压缩器", "控制动态范围与瞬态。", "高级", "compressor.getState", "compressor.setState",
            new("thresholdDb", "阈值", -72, 0, 0.1, "dB"), new("ratio", "压缩比", 1, 40, 0.1, ":1"),
            new("attackMs", "启动时间", 0.1, 500, 0.1, "ms"), new("releaseMs", "释放时间", 5, 5000, 1, "ms"),
            new("kneeDb", "拐点", 0, 24, 0.1, "dB"), new("makeupDb", "补偿增益", -24, 24, 0.1, "dB"), new("mix", "干湿比例", 0, 1, 0.01, "")),
        new("crossfeed", "交叉馈送", "让耳机中的左右声道自然交融。", "高级", "crossfeed.getState", "crossfeed.setState",
            new("amount", "混合强度", 0, 1, 0.01, ""), new("cutoffHz", "截止频率", 100, 2000, 1, "Hz")),
        new("stereoField", "立体声场", "控制宽度、中置与侧向能量。", "高级", "stereoField.getState", "stereoField.setState",
            new("width", "声场宽度", 0, 2, 0.01, "×"), new("centerGainDb", "中置增益", -18, 18, 0.1, "dB"),
            new("sideGainDb", "侧向增益", -18, 18, 0.1, "dB")),
        new("channelMatrix", "声道矩阵", "配置左右输入到左右输出的混合系数。", "高级", "channelMatrix.getState", "channelMatrix.setState",
            new("leftToLeft", "L → L", -2, 2, 0.01, ""), new("rightToLeft", "R → L", -2, 2, 0.01, ""),
            new("leftToRight", "L → R", -2, 2, 0.01, ""), new("rightToRight", "R → R", -2, 2, 0.01, ""))];
}
