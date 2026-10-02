using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Theming;

public sealed record ThemePreset(string Id, string Name, ThemeTone Light, ThemeTone Dark)
{ public override string ToString() => Name; }
public sealed record ThemeColorDefinition(string Key, string Label, string Group);
public sealed record ThemeNumberDefinition(string Key, string Label, double Min, double Max, double Step, string Unit);

public static class ThemeCatalog
{
    public static IReadOnlyList<ThemePreset> Presets { get; } = Load();
    public static ThemePreset Find(string? id) => Presets.FirstOrDefault(p => p.Id == id) ?? Presets.First(p => p.Id == "classic");
    public static IReadOnlyList<ThemeColorDefinition> Colors { get; } = [
        new("appBg", "主背景", "基础配色"), new("accent", "强调色", "基础配色"), new("accentStrong", "强调文字", "基础配色"),
        new("secondary", "辅助色", "基础配色"), new("heading", "标题", "基础配色"), new("muted", "次要文字", "基础配色"),
        new("panel", "面板", "基础配色"), new("appBg2", "背景第二色", "渐变与文字"), new("appBg3", "背景第三色", "渐变与文字"),
        new("panelSoft", "柔和面板", "渐变与文字"), new("text", "正文", "渐变与文字"), new("border", "边框", "渐变与文字"),
        new("onAccent", "强调按钮文字", "渐变与文字"), new("buttonText", "普通按钮文字", "渐变与文字"),
        new("titlebar", "标题栏", "界面区域"), new("sidebar", "侧栏", "界面区域"), new("player", "播放栏", "界面区域"),
        new("field", "输入框", "界面区域"), new("row", "列表行", "界面区域"), new("rowHover", "悬停行", "界面区域"),
        new("rowActive", "选中行", "界面区域"), new("chip", "标签", "界面区域"), new("success", "成功", "状态与焦点"),
        new("warning", "提醒", "状态与焦点"), new("danger", "错误", "状态与焦点"), new("focus", "键盘焦点", "状态与焦点")];
    public static IReadOnlyList<ThemeNumberDefinition> Numbers { get; } = [
        new("panelOpacityPercent", "面板不透明度", 40, 100, 1, "%"), new("glassPercent", "玻璃感", 0, 80, 1, "%"),
        new("shadowPercent", "阴影强度", 0, 100, 1, "%"), new("cornerRadiusPx", "面板圆角", 0, 28, 1, "px"),
        new("panelBlurPx", "背景模糊", 0, 32, 1, "px"), new("saturationPercent", "配色饱和度", 60, 140, 1, "%"),
        new("motionSpeedSeconds", "过渡时长", .12, 8, .01, "s"), new("motionIntensityPercent", "动效幅度", 0, 160, 1, "%")];
    private static IReadOnlyList<ThemePreset> Load()
    {
        using var stream = typeof(ThemeCatalog).Assembly.GetManifestResourceStream("Echo.Desktop.LegacyPresets.json")!;
        return JsonSerializer.Deserialize(stream, ThemeJsonContext.Default.ThemePresetArray)!.OrderBy(p => p.Id != "classic").ToArray();
    }
    public static ThemeTone Normalize(ThemeTone? tone, ThemeTone fallback)
    {
        var colors = Colors.ToDictionary(d => d.Key, d => tone?.Colors?.TryGetValue(d.Key, out var value) == true
            && ThemeColorMath.TryHex(value, out var hex) ? hex : fallback.Colors[d.Key]);
        var numbers = Numbers.ToDictionary(d => d.Key, d => tone?.Numbers?.TryGetValue(d.Key, out var value) == true && double.IsFinite(value)
            ? Math.Clamp(value, d.Min, d.Max) : fallback.Numbers[d.Key]);
        return new(colors, numbers, tone?.MotionEnabled ?? fallback.MotionEnabled);
    }
}

[JsonSerializable(typeof(ThemePreset[]))]
internal partial class ThemeJsonContext : JsonSerializerContext;
