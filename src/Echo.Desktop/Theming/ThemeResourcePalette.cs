using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Theming;

internal static class ThemeResourcePalette
{
    public static ResourceDictionary Create(ThemeTone tone, ThemePreferences preferences, bool dark, Color? ambientAccent)
    {
        var result = new ResourceDictionary(); var colors = new Dictionary<string, string>(tone.Colors);
        if (preferences.Mode == "ambient" && ambientAccent is { } accent)
        {
            colors["accent"] = accent.ToString();
            colors["accentStrong"] = ThemeColorMath.Mix(accent, dark ? Colors.White : Colors.Black, .3).ToString();
            foreach (var key in new[] { "appBg", "appBg2", "appBg3", "panel", "sidebar", "player" })
                colors[key] = ThemeColorMath.Mix(Color.Parse(colors[key]), accent, .10).ToString();
        }
        Color C(string key) => ThemeColorMath.Saturate(Color.Parse(colors[key]), tone.Numbers["saturationPercent"] / 100);
        var alpha = tone.Numbers["panelOpacityPercent"] / 100 * (1 - tone.Numbers["glassPercent"] / 200);
        void Brush(string resource, string key, double opacity = 1) { var c = C(key); result[resource] = new SolidColorBrush(c, opacity); }
        foreach (var (resource, key) in new[] { ("EchoText", "text"), ("EchoHeading", "heading"), ("EchoMuted", "muted"),
            ("EchoSubtle", "secondary"), ("EchoAccent", "accent"), ("EchoAccentText", "accentStrong"), ("EchoIconAccent", "accent"),
            ("EchoBorder", "border"), ("EchoDivider", "border"), ("EchoHover", "rowHover"), ("EchoSelected", "rowActive"),
            ("EchoField", "field"), ("EchoRow", "row"), ("EchoChip", "chip"), ("EchoFocus", "focus"), ("EchoDanger", "danger"),
            ("EchoSuccess", "success"), ("EchoWarning", "warning"), ("EchoButtonText", "buttonText"), ("EchoOnAccent", "onAccent") }) Brush(resource, key);
        Brush("EchoPanelMuted", "panelSoft", alpha); Brush("EchoTitlebar", "titlebar", alpha); Brush("EchoSidebar", "sidebar", alpha); Brush("EchoPlayer", "player", alpha);
        Brush("EchoQueueChip", "chip");
        Brush("EchoPage", "appBg", preferences.Mode == "acrylic" ? .65 : 1);
        result["EchoPageBase"] = new LinearGradientBrush
        { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            Opacity = preferences.Mode == "acrylic" ? .65 : 1, GradientStops = new() { new(C("appBg"), 0), new(C("appBg2"), .5), new(C("appBg3"), 1) } };
        result["EchoPageContent"] = preferences.Wallpaper.Length > 0 ? Brushes.Transparent : result["EchoPageBase"];
        result["EchoPageGlow"] = Brushes.Transparent;
        foreach (var (resource, key, opacity) in new[] { ("SettingsNavBackground", "panelSoft", alpha), ("SettingsSurface", "panel", alpha),
            ("SettingsHeaderSurface", "panelSoft", alpha), ("SettingsBorder", "border", .52), ("SettingsDivider", "border", .30),
            ("SettingsSelected", "rowActive", 1d), ("SettingsToggleBackground", "chip", 1d), ("SettingsToggleTrack", "border", 1d),
            ("SettingsBadge", "warning", .15), ("SettingsBadgeText", "warning", 1d) }) Brush(resource, key, opacity);
        result["EchoPanelRadius"] = new CornerRadius(tone.Numbers["cornerRadiusPx"]);
        result["EchoPanelShadow"] = new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 16,
            Color = Color.FromArgb((byte)(tone.Numbers["shadowPercent"] * .25), 0, 0, 0) });
        result["EchoFont"] = new FontFamily((preferences.Font == "Outfit" ? "avares://Echo.Desktop/Resources/Fonts#Outfit" : preferences.Font)
            + ", " + preferences.ChineseFont + ", " + preferences.FallbackFont + ", Segoe UI, PingFang SC, DejaVu Sans");
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            Brush("ExpanderHeaderBackground" + state, state == "" ? "panelSoft" : "rowHover");
            Brush("ExpanderHeaderForeground" + state, "text"); Brush("ExpanderHeaderBorderBrush" + state, "border", .5);
            Brush("ExpanderChevronForeground" + state, "muted");
            result["ExpanderChevronBackground" + state] = Brushes.Transparent;
            result["ExpanderChevronBorderBrush" + state] = Brushes.Transparent;
        }
        Brush("ExpanderContentBackground", "panel", alpha); Brush("ExpanderContentBorderBrush", "border", .5);
        return result;
    }
}
