namespace Echo.Contracts.Settings;

// Small immutable snapshots; UI edits replace dictionaries rather than mutate persisted state.
public sealed record ThemeTone(IReadOnlyDictionary<string, string> Colors,
    IReadOnlyDictionary<string, double> Numbers, bool MotionEnabled = true);
public sealed record CustomTheme(string Id, string Name, string BasePreset, ThemeTone Light, ThemeTone Dark);
public sealed record ThemePair(ThemeTone Light, ThemeTone Dark);
public sealed record ThemePreferences(string Mode = "light", string Preset = "classic",
    ThemeTone? Light = null, ThemeTone? Dark = null, bool ScheduleEnabled = false,
    string DarkAt = "19:00", string LightAt = "07:00", bool Invert = false,
    string Font = "Outfit", string ChineseFont = "Microsoft YaHei UI", string FallbackFont = "Noto Sans SC",
    string Wallpaper = "", double WallpaperOpacity = 20, double WallpaperBlur = 0,
    string WallpaperFit = "UniformToFill", double WallpaperX = 50, double WallpaperY = 50,
    IReadOnlyList<CustomTheme>? Library = null, string? SelectedCustomId = null,
    IReadOnlyDictionary<string, ThemePair>? Overrides = null, bool AcrylicEnabled = false,
    double AcrylicTransparency = 70, bool AcrylicKeepUnfocused = false,
    AppearancePreferences? Appearance = null);
