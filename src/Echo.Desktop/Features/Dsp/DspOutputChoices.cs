using Echo.Playback.Processing;

namespace Echo.Desktop.Features.Dsp;

internal static class DspOutputChoices
{
    public static DspChoiceDefinition Filter(string key, string label) => new(key, label,
        FirProfileCatalog.Profiles.Select(x => new DspChoice(x.Id, x.Id.Replace("echo-", "ECHO ") + (x.Phase == "minimum" ? " · 最小相位" : " · 线性相位"))).ToArray());
    public static DspChoiceDefinition Compute() => new("computeBackend", "计算后端", new("cpu", "CPU"), new("cuda", "CUDA · 需宿主支持"));
    public static DspChoiceDefinition Sound() => new("soundProfile", "听音预设", [new("custom", "自定义"), .. FirProfileCatalog.Sounds.Select(x => new DspChoice(x.Id, x.Id))]);
    public static DspChoiceDefinition Tier() => new("firTier", "FIR 强度", new("standard", "标准"), new("balanced-heavy", "载波侧增强"), new("heavy", "全链增强"));
}
