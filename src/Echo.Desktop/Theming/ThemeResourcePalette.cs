using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Theming;

internal static class ThemeResourcePalette
{
    public static ResourceDictionary Create(ThemeTone tone, ThemePreferences preferences, bool dark, Color? ambientAccent, bool acrylicAvailable = false, bool wallpaperAvailable = true)
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
        var appearance = preferences.Appearance ?? new();
        var wallpaper = wallpaperAvailable && appearance.WallpaperEnabled && (preferences.Wallpaper.Length > 0 || appearance.PortraitWallpaper.Length > 0);
        if ((dark ? appearance.PageTextDark : appearance.PageTextLight) is { } textColor) colors["text"] = textColor;
        var alpha = tone.Numbers["panelOpacityPercent"] / 100 * (1 - tone.Numbers["glassPercent"] / 200);
        var wallpaperAlpha = Math.Max(appearance.WallpaperVisualProtection ? .36 : 0, appearance.WallpaperUiOpacity / 100);
        if (wallpaper) alpha *= wallpaperAlpha;
        var acrylic = (preferences.AcrylicEnabled || preferences.Mode == "acrylic") && acrylicAvailable;
        var backdropOpacity = acrylic ? 1 - preferences.AcrylicTransparency / 100 : 1;
        if (acrylic) alpha *= backdropOpacity;
        void Brush(string resource, string key, double opacity = 1) { var c = C(key); result[resource] = new SolidColorBrush(c, opacity); }
        foreach (var (resource, key) in new[] { ("EchoText", "text"), ("EchoHeading", "heading"), ("EchoMuted", "muted"),
            ("EchoSubtle", "muted"), ("EchoAccent", "accent"), ("EchoAccentText", "accentStrong"), ("EchoIconAccent", "accent"),
            ("EchoHover", "rowHover"), ("EchoSelected", "rowActive"),
            ("EchoField", "field"), ("EchoRow", "row"), ("EchoChip", "chip"), ("EchoFocus", "focus"), ("EchoDanger", "danger"),
            ("EchoSuccess", "success"), ("EchoWarning", "warning"), ("EchoButtonText", "buttonText"), ("EchoOnAccent", "onAccent") }) Brush(resource, key);
        var border = ThemeColorMath.SurfaceBorder(C("border"), C("appBg"), dark ? 1.65 : 1.4);
        var divider = ThemeColorMath.SurfaceBorder(C("border"), C("appBg"), dark ? 1.35 : 1.18);
        result["EchoBorder"] = new SolidColorBrush(border);
        result["EchoDivider"] = new SolidColorBrush(divider);
        result["SettingsBorder"] = new SolidColorBrush(border);
        result["SettingsDivider"] = new SolidColorBrush(divider);
        Brush("LibraryInputSurface", "field");
        Brush("EchoPanelMuted", "panelSoft", alpha); Brush("EchoTitlebar", "titlebar", alpha); Brush("EchoSidebar", "sidebar", alpha); Brush("EchoPlayer", "player", alpha);
        result["EchoSidebarSearch"] = new SolidColorBrush(ThemeColorMath.Mix(C("sidebar"), C("heading"), .03));
        Brush("EchoSidebarSelected", "rowActive", .5);
        Brush("LibraryPopupSelected", "accent", .12);
        Brush("LibraryInputSurface", "chip", .45);
        Brush("EchoQueueChip", "chip");
        Brush("EchoPage", "appBg", backdropOpacity);
        if (acrylic) result["EchoPage"] = Brushes.Transparent;
        result["EchoPageBase"] = new LinearGradientBrush
        { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            Opacity = backdropOpacity, GradientStops = new() { new(C("appBg"), 0),
                new(ThemeColorMath.PageGradientStop(C("appBg"), C("appBg2")), .5),
                new(ThemeColorMath.PageGradientStop(C("appBg"), C("appBg3")), 1) } };
        result["EchoPageContent"] = wallpaper ? new SolidColorBrush(C("appBg"), wallpaperAlpha * (appearance.WallpaperUnifiedOpacity ? 1 : .62))
            : acrylic ? Brushes.Transparent : result["EchoPageBase"];
        result["EchoPageGlow"] = Brushes.Transparent;
        if (wallpaper)
        {
            result["EchoTitlebar"] = new SolidColorBrush(C("titlebar"), wallpaperAlpha * (appearance.WallpaperUnifiedOpacity ? 1 : .74));
            result["EchoSidebar"] = new SolidColorBrush(C("sidebar"), wallpaperAlpha * (appearance.WallpaperUnifiedOpacity ? 1 : .68));
            result["EchoPlayer"] = new SolidColorBrush(C("player"), wallpaperAlpha * (appearance.WallpaperUnifiedOpacity ? 1 : .78));
        }
        result["EchoCoverRadius"] = new CornerRadius(appearance.CoverShape == "rounded" ? 8 : 0);
        foreach (var (resource, key, opacity) in new[] { ("SettingsNavBackground", "panelSoft", alpha), ("SettingsSurface", "panel", alpha),
            ("SettingsHeaderSurface", "panelSoft", alpha),
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
            Brush("ExpanderHeaderForeground" + state, "text"); result["ExpanderHeaderBorderBrush" + state] = new SolidColorBrush(border);
            Brush("ExpanderChevronForeground" + state, "muted");
            result["ExpanderChevronBackground" + state] = Brushes.Transparent;
            result["ExpanderChevronBorderBrush" + state] = Brushes.Transparent;
        }
        Brush("ExpanderContentBackground", "panel", alpha); result["ExpanderContentBorderBrush"] = new SolidColorBrush(border);
        Brush("TextControlBackgroundDisabled", "field", .6); Brush("TextControlForegroundDisabled", "muted");
        Brush("TextControlBorderBrushDisabled", "border", .45);
        SemiResourcePalette.Apply(result, C, dark);
        return result;
    }
}
