namespace Echo.Desktop.Features.Dsp;

internal static class NativeDspModules
{
    public static IEnumerable<DspModule> Create()
    {
        yield return new("limiter", "真峰值限制器", "设置输出峰值上限与释放时间。", "高级", "dsp.getState", "dsp.setSafetyLimiter",
            new("dspLimiterCeilingDb", "峰值上限", -6, -0.1, 0.1, "dBTP"),
            new("dspLimiterReleaseMs", "释放时间", 10, 400, 1, "ms")) {
            Toggles = [new("limiterAutoCeiling", "自动峰值上限")] };
        yield return new("multiband", "多段压缩", "分别控制低、中、高频段的动态。", "高级", "multiband.getState", "multiband.setState", MultibandParameters())
        { Toggles = [new("bands.0.enabled", "启用低频段"), new("bands.1.enabled", "启用中频段"), new("bands.2.enabled", "启用高频段")] };
        yield return new("deesser", "齿音抑制", "控制高频齿音与尖锐瞬态。", "高级", "deesser.getState", "deesser.setState",
            new("frequencyHz", "检测频率", 2000, 12000, 1, "Hz"), new("q", "检测 Q", 0.5, 12, 0.1, ""),
            new("thresholdDb", "阈值", -48, 0, 0.1, "dB"), new("ratio", "压缩比", 1, 20, 0.1, ":1"),
            new("attackMs", "启动时间", 0.1, 30, 0.1, "ms"), new("releaseMs", "释放时间", 5, 200, 1, "ms"),
            new("kneeDb", "拐点", 0, 18, 0.1, "dB"), new("rangeDb", "最大衰减", 0, 24, 0.1, "dB"),
            new("mix", "干湿比例", 0, 1, 0.01, ""));
        yield return new("virtualStage", "虚拟声场", "以声源方向与房间反射营造耳机空间感。", "高级", "virtualStage.getState", "virtualStage.setState",
            new("strength", "空间强度", 0, 1, 0.01, ""), new("azimuthDegrees", "声源角度", 15, 75, 1, "°"),
            new("reflection", "房间反射", 0, 1, 0.01, "")) { Toggles = [new("speakersBypassed", "音箱输出旁路")] };
    }

    private static DspParameterDefinition[] MultibandParameters()
    {
        var fields = new List<DspParameterDefinition>
        {
            new("lowCrossoverHz", "低频分界", 40, 500, 1, "Hz"),
            new("highCrossoverHz", "高频分界", 800, 8000, 1, "Hz"),
            new("mix", "干湿比例", 0, 1, 0.01, ""), new("stereoLink", "立体声链接", 0, 1, 0.01, "")
        };
        var names = new[] { "低频", "中频", "高频" };
        for (var band = 0; band < names.Length; band++)
        {
            var prefix = $"bands.{band}.";
            fields.Add(new(prefix + "thresholdDb", names[band] + "阈值", -60, 0, 0.1, "dB"));
            fields.Add(new(prefix + "ratio", names[band] + "压缩比", 1, 20, 0.1, ":1"));
            fields.Add(new(prefix + "attackMs", names[band] + "启动", 0.1, 200, 0.1, "ms"));
            fields.Add(new(prefix + "releaseMs", names[band] + "释放", 5, 2000, 1, "ms"));
            fields.Add(new(prefix + "makeupDb", names[band] + "补偿", -12, 12, 0.1, "dB"));
            fields.Add(new(prefix + "kneeDb", names[band] + "拐点", 0, 18, 0.1, "dB"));
        }
        return fields.ToArray();
    }
}
