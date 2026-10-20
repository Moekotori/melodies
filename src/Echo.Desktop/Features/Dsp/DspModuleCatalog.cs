using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspModule : ObservableObject
{
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Group { get; }
    public bool HasGroupHeading => Id is "replayGain" or "src" or "eq" or "room";
    public bool HasAdvancedHeading => Id == "compressor";
    public bool NavigationItemVisible => HasAdvancedHeading || NavigationVisible;
    public string? ReadMethod { get; }
    public string? WriteMethod { get; }
    public IReadOnlyList<DspParameterDefinition> Parameters { get; }
    public IReadOnlyList<DspToggleDefinition> Toggles { get; init; } = [];
    public IReadOnlyList<DspChoiceDefinition> Choices { get; init; } = [];
    public bool ConfiguresOutput => Id is "src" or "sdm";
    public bool DraftOnly => ConfiguresOutput || Id == "rack";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NavigationItemVisible))] private bool navigationVisible;
    public bool EditsEqualizer => Id is "eq" or "headphone";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsActive)), NotifyPropertyChangedFor(nameof(Detail))] private string status = "未读取";
    public bool IsActive => Id != "safety" && Status is "已启用" or "歌曲增益" or "专辑增益";
    public bool ShowsDetail => Id is not ("listening" or "rack");
    public string Detail => !ShowsDetail ? "" : Status == "旁路" ? "已旁路" : Status;
    public bool HasBadge => Id == "sdm";
    public string Badge => "测试版";
    public string Icon => Id switch
    {
        "listening" => "sparkles", "rack" => "sequence", "replayGain" or "headroom" or "compressor" => "gauge",
        "src" or "channel" => "speaker", "sdm" or "room" or "stereoField" => "waves", "eq" => "sliders",
        "headphone" or "crossfeed" => "headphones", "safety" => "lock", "channelMatrix" => "link", _ => "sliders",
    };
    public DspModule(string id, string title, string description, string group, string? read = null,
        string? write = null, params DspParameterDefinition[] parameters)
    { Id = id; Title = title; Description = description; Group = group; ReadMethod = read; WriteMethod = write; Parameters = parameters; navigationVisible = group != "高级"; Status = read is null && id is not ("listening" or "src" or "sdm") ? "待迁移" : "未读取"; }
}

public sealed record DspParameterDefinition(string Key, string Label, double Minimum, double Maximum, double Step, string Unit);
public sealed record DspToggleDefinition(string Key, string Label);
public sealed record DspChoiceDefinition(string Key, string Label, params DspChoice[] Options);
public sealed record DspChoice(string Key, string Label);

internal static class DspModuleCatalog
{
    public static IReadOnlyList<DspModule> Create() => Build().OrderBy(x => Array.IndexOf(Order, x.Id)).ToArray();
    private static readonly string[] Order = ["listening", "rack", "replayGain", "headroom", "src", "sdm", "eq", "headphone", "room", "channel", "safety", "compressor", "multiband", "deesser", "limiter", "crossfeed", "virtualStage", "stereoField", "channelMatrix"];
    private static IReadOnlyList<DspModule> Build() => [
        new("listening", "听音方案", "把当前处理链存成方案，之后一键套回原生宿主。", "总览"),
        new("rack", "DSP Rack", "调整实际处理顺序。后面的保护阶段固定不动。", "总览", "dspRack.getState", "dspRack.setState"),
        new("replayGain", "音量均衡", "按曲目或专辑增益统一播放响度。", "电平", "replayGain.getConfig", "replayGain.setConfig",
            new DspParameterDefinition("preampDb", "额外增益", -12, 12, 0.1, "dB")),
        new("headroom", "Headroom", "为处理链预留峰值空间。", "电平", "dsp.getState", "dsp.setHeadroom",
            new DspParameterDefinition("headroomDb", "处理链增益", -12, 12, 0.1, "dB")),
        new("src", "升频", "整数倍升频和 PCM 抖动。应用时会重配当前输出。", "采样转换", null, "output.configure") {
            Choices = [
                new("ratio", "升频", new DspChoice("1", "关闭"), new DspChoice("2", "2×"), new DspChoice("4", "4×"), new DspChoice("8", "8×"), new DspChoice("16", "16×"), new DspChoice("compat48", "48 kHz 兼容")),
                new("ditherMode", "抖动", new DspChoice("off", "关闭"), new DspChoice("tpdf", "TPDF"), new DspChoice("highpass-tpdf", "高通 TPDF"), new DspChoice("ns-5", "噪声整形 5"), new DspChoice("ns-9", "噪声整形 9"), new DspChoice("ultra-shaped", "超整形")),
                new("ditherBits", "量化位深", new DspChoice("16", "16 bit"), new DspChoice("24", "24 bit")), DspOutputChoices.Filter("filter1x", "1× 输入滤波器"), DspOutputChoices.Filter("filterNx", "高倍率滤波器"), DspOutputChoices.Compute()] },
        new("sdm", "DSD 转换", "把 PCM 调制成 DoP。需要独占或 ASIO，应用时会重配输出。", "采样转换", null, "output.configure") {
            Choices = [
                new("outputFormat", "输出格式", new DspChoice("pcm", "PCM"), new DspChoice("dop24le", "DoP"), new DspChoice("dsd-native-raw", "Native DSD")),
                new("carrierRate", "DSD 传输率", new DspChoice("176400", "176.4 kHz"), new DspChoice("192000", "192 kHz"), new DspChoice("352800", "352.8 kHz"), new DspChoice("384000", "384 kHz"), new DspChoice("705600", "705.6 kHz"), new DspChoice("768000", "768 kHz"), new DspChoice("1411200", "1411.2 kHz"), new DspChoice("1536000", "1536 kHz")),
                new("qualityProfile", "调制质量", new DspChoice("safe", "安全"), new DspChoice("hifi", "Hi-Fi"), new DspChoice("reference", "参考"), new DspChoice("insane", "极致"), new DspChoice("adaptive", "自适应"), new DspChoice("adaptive-light", "轻量自适应"), new DspChoice("adaptive-super", "增强自适应"), new DspChoice("reference-ec", "参考 EC")),
                new("extraLoop", "决策环路", new DspChoice("off", "关闭"), new DspChoice("extra", "Extra"), new DspChoice("horizon", "Horizon")), DspOutputChoices.Sound(), DspOutputChoices.Filter("filter1x", "1× 输入滤波器"), DspOutputChoices.Filter("filterNx", "高倍率滤波器"), DspOutputChoices.Compute(), DspOutputChoices.Tier(), new("preModulatorProfile", "前调制器", new DspChoice("standard", "标准"), new DspChoice("heavy", "Heavy"), new DspChoice("ultimate-reference", "Ultimate Reference"), new DspChoice("ultimate-transient", "Ultimate Transient"))] },
        new("eq", "参数 EQ", "频段、前级与预设。", "音色", "eq.getState", "eq.setState"),
        new("headphone", "耳机校正", "导入频响曲线，应用到独立耳机校正处理链。", "音色", "headphoneCorrection.getState", "headphoneCorrection.setState"),
        new("room", "房间校正", "导入脉冲响应，调整增益后卷积进播放。", "空间与输出", "roomCorrection.getState", "roomCorrection.setEnabled",
            new DspParameterDefinition("trimDb", "脉冲增益", -24, 6, 0.1, "dB")),
        new("channel", "声道工具", "左右增益、平衡、延时和分频补偿。", "空间与输出", "channelBalance.getState", "channelBalance.setState",
            new("balance", "左右平衡", -1, 1, 0.01, ""), new("leftGainDb", "左声道增益", -12, 6, 0.1, "dB"),
            new("rightGainDb", "右声道增益", -12, 6, 0.1, "dB"), new("leftDelayMs", "左声道延时", 0, 10, 0.1, "ms"),
            new("rightDelayMs", "右声道延时", 0, 10, 0.1, "ms"),
            new("low.left", "低频 · 左", -6, 3, 0.1, "dB"), new("low.right", "低频 · 右", -6, 3, 0.1, "dB"),
            new("mid.left", "中频 · 左", -6, 3, 0.1, "dB"), new("mid.right", "中频 · 右", -6, 3, 0.1, "dB"),
            new("high.left", "高频 · 左", -6, 3, 0.1, "dB"), new("high.right", "高频 · 右", -6, 3, 0.1, "dB")) {
            Toggles = [new("swapLeftRight", "交换左右"), new("invertLeft", "反相左"), new("invertRight", "反相右"), new("constantPower", "等功率平衡")],
            Choices = [new("monoMode", "单声道", new DspChoice("off", "关闭"), new DspChoice("sum", "混合"), new DspChoice("left", "仅左"), new DspChoice("right", "仅右"))] },
        new("safety", "输出安全", "原生安全限幅与削波风险。", "空间与输出", "dsp.getState", "dsp.setSafetyLimiter"),
        new("compressor", "压缩器", "控制动态范围与瞬态。", "高级", "compressor.getState", "compressor.setState",
            new("thresholdDb", "阈值", -72, 0, 0.1, "dB"), new("ratio", "压缩比", 1, 40, 0.1, ":1"),
            new("attackMs", "启动时间", 0.1, 500, 0.1, "ms"), new("releaseMs", "释放时间", 5, 5000, 1, "ms"),
            new("kneeDb", "拐点", 0, 24, 0.1, "dB"), new("makeupDb", "补偿增益", -24, 24, 0.1, "dB"), new("mix", "干湿比例", 0, 1, 0.01, ""),
            new("rangeDb", "最大衰减", 0, 72, 0.1, "dB"), new("stereoLink", "立体声链接", 0, 1, 0.01, ""),
            new("sidechainHighpassHz", "侧链高通", 20, 400, 1, "Hz")) {
            Toggles = [new("sidechainHighpassEnabled", "启用侧链高通"), new("autoRelease", "自动释放")],
            Choices = [new("detectorMode", "检测", new DspChoice("peak", "峰值"), new DspChoice("rms", "RMS"))] },
        new("crossfeed", "交叉馈送", "让耳机中的左右声道自然交融。", "高级", "crossfeed.getState", "crossfeed.setState",
            new("amount", "混合强度", 0, 1, 0.01, ""), new("cutoffHz", "截止频率", 100, 4000, 1, "Hz"),
            new("feedDb", "交叉衰减", 1, 15, 0.1, "dB")) {
            Choices = [new("model", "算法", new DspChoice("legacy", "经典"), new DspChoice("bs2b", "BS2B"))] },
        new("stereoField", "立体声场", "控制宽度、中置与侧向能量。", "高级", "stereoField.getState", "stereoField.setState",
            new("width", "声场宽度", 0, 2, 0.01, "×"), new("centerGainDb", "中置增益", -18, 18, 0.1, "dB"),
            new("sideGainDb", "侧向增益", -18, 18, 0.1, "dB")),
        new("channelMatrix", "声道矩阵", "配置左右输入到左右输出的混合系数。", "高级", "channelMatrix.getState", "channelMatrix.setState",
            new("leftToLeft", "L → L", -2, 2, 0.01, ""), new("rightToLeft", "R → L", -2, 2, 0.01, ""),
            new("leftToRight", "L → R", -2, 2, 0.01, ""), new("rightToRight", "R → R", -2, 2, 0.01, "")),
        .. NativeDspModules.Create()];
}
