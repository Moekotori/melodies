using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Theming;

// Window owner of appearance settings. No files, timers, playback or global mutable state.
public sealed partial class ThemeViewModel : ObservableObject
{
    private ThemePreferences _preferences = new();
    private readonly Queue<ThemePreferences> _undo = new();
    private bool _sync;
    public event Action? Changed;
    public ThemePreferences Snapshot => _preferences;
    public IReadOnlyList<ThemePreset> Presets => ThemeCatalog.Presets;
    public IReadOnlyList<string> Modes { get; } = ["浅色", "深色", "跟随系统", "氛围", "亚克力"];
    public IReadOnlyList<string> Tones { get; } = ["浅色", "深色"];
    public IReadOnlyList<string> Fonts { get; } = ["Outfit", "Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans SC", "PingFang SC", "DejaVu Sans"];
    public IReadOnlyList<string> WallpaperFits { get; } = ["UniformToFill", "Uniform", "Fill", "Tile", "Center"];
    public IReadOnlyList<CustomTheme> Library => _preferences.Library ?? [];
    [ObservableProperty] private string mode = "浅色";
    [ObservableProperty] private ThemePreset selectedPreset = ThemeCatalog.Find("classic");
    [ObservableProperty] private string editTone = "浅色";
    [ObservableProperty] private bool scheduleEnabled;
    [ObservableProperty] private string darkAt = "19:00";
    [ObservableProperty] private string lightAt = "07:00";
    [ObservableProperty] private bool invert;
    [ObservableProperty] private string font = "Outfit";
    [ObservableProperty] private string chineseFont = "Microsoft YaHei UI";
    [ObservableProperty] private string fallbackFont = "Noto Sans SC";
    [ObservableProperty] private string wallpaper = "";
    [ObservableProperty] private double wallpaperOpacity = 20;
    [ObservableProperty] private double wallpaperBlur;
    [ObservableProperty] private double wallpaperX = 50;
    [ObservableProperty] private double wallpaperY = 50;
    [ObservableProperty] private string wallpaperFit = "UniformToFill";
    [ObservableProperty] private bool motionEnabled = true;
    [ObservableProperty] private bool acrylicEnabled;
    [ObservableProperty] private double acrylicTransparency = 70;
    [ObservableProperty] private bool acrylicKeepUnfocused;
    [ObservableProperty] private string acrylicStatus = "";
    [ObservableProperty] private string name = "我的主题";
    [ObservableProperty] private string feedback = "";
    [ObservableProperty] private IReadOnlyList<ThemeColorGroup> colorGroups = [];
    [ObservableProperty] private IReadOnlyList<ThemeNumberField> numberFields = [];
    public bool CanUndo => _undo.Count > 0;
    public ThemeTone Editing => Tone(EditTone == "深色");
    public string ContrastStatus
    {
        get
        {
            var c = Editing.Colors;
            var pairs = new[] { ("正文", "text", "appBg", 4.5), ("标题", "heading", "appBg", 4.5),
                ("普通按钮", "buttonText", "panel", 4.5), ("强调按钮", "onAccent", "accent", 3d) };
            var warnings = pairs.Where(p => ThemeColorMath.Contrast(c[p.Item2], c[p.Item3]) < p.Item4).Select(p => p.Item1).ToArray();
            return warnings.Length == 0 ? "文字与背景对比清晰" : "对比度偏低：" + string.Join("、", warnings);
        }
    }
    public ThemeViewModel() => RebuildEditor();
    public ThemeTone Tone(bool dark) => ThemeCatalog.Normalize(dark ? _preferences.Dark : _preferences.Light,
        dark ? SelectedPreset.Dark : SelectedPreset.Light);
    public void Restore(ThemePreferences? preferences, bool legacyDark = false)
    {
        _sync = true;
        _preferences = preferences ?? new(Mode: legacyDark ? "dark" : "light");
        SelectedPreset = ThemeCatalog.Find(_preferences.Preset);
        _preferences = _preferences with { Preset = SelectedPreset.Id, Light = Tone(false), Dark = Tone(true),
            Library = (_preferences.Library ?? []).Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Id)).Take(16)
                .Select(t => t with { Name = t.Name?.Length > 60 ? t.Name[..60] : t.Name ?? "我的主题",
                    Light = ThemeCatalog.Normalize(t.Light, ThemeCatalog.Find(t.BasePreset).Light), Dark = ThemeCatalog.Normalize(t.Dark, ThemeCatalog.Find(t.BasePreset).Dark) }).ToArray(),
            Overrides = (_preferences.Overrides ?? new Dictionary<string, ThemePair>()).Where(p => Presets.Any(t => t.Id == p.Key) && p.Value is not null).Take(49)
                .ToDictionary(p => p.Key, p => new ThemePair(ThemeCatalog.Normalize(p.Value.Light, ThemeCatalog.Find(p.Key).Light), ThemeCatalog.Normalize(p.Value.Dark, ThemeCatalog.Find(p.Key).Dark))) };
        Mode = _preferences.Mode switch { "dark" => "深色", "system" => "跟随系统", "ambient" => "氛围", "acrylic" => "亚克力", _ => "浅色" };
        ScheduleEnabled = _preferences.ScheduleEnabled;
        DarkAt = TimeOnly.TryParseExact(_preferences.DarkAt, "HH:mm", out _) ? _preferences.DarkAt : "19:00";
        LightAt = TimeOnly.TryParseExact(_preferences.LightAt, "HH:mm", out _) ? _preferences.LightAt : "07:00";
        Invert = _preferences.Invert; Font = SafeFont(_preferences.Font); ChineseFont = SafeFont(_preferences.ChineseFont); FallbackFont = SafeFont(_preferences.FallbackFont);
        Wallpaper = LocalPath(_preferences.Wallpaper);
        WallpaperOpacity = Finite(_preferences.WallpaperOpacity, 0, 100, 20); WallpaperBlur = Finite(_preferences.WallpaperBlur, 0, 40, 0);
        WallpaperX = Finite(_preferences.WallpaperX, 0, 100, 50); WallpaperY = Finite(_preferences.WallpaperY, 0, 100, 50);
        WallpaperFit = WallpaperFits.Contains(_preferences.WallpaperFit) ? _preferences.WallpaperFit : WallpaperFits[0];
        AcrylicEnabled = _preferences.AcrylicEnabled; AcrylicKeepUnfocused = _preferences.AcrylicKeepUnfocused;
        AcrylicTransparency = Finite(_preferences.AcrylicTransparency, 0, 100, 70);
        _preferences = _preferences with { Mode = Mode switch { "深色" => "dark", "跟随系统" => "system", "氛围" => "ambient", "亚克力" => "acrylic", _ => "light" },
            DarkAt = DarkAt, LightAt = LightAt, Font = Font, ChineseFont = ChineseFont, FallbackFont = FallbackFont, Wallpaper = Wallpaper ?? "",
            WallpaperOpacity = WallpaperOpacity, WallpaperBlur = WallpaperBlur, WallpaperX = WallpaperX, WallpaperY = WallpaperY, WallpaperFit = WallpaperFit,
            SelectedCustomId = Library.Any(t => t.Id == _preferences.SelectedCustomId) ? _preferences.SelectedCustomId : null,
            AcrylicTransparency = AcrylicTransparency };
        RestoreAppearance(); _sync = false; RebuildEditor(); OnPropertyChanged(nameof(Library)); Changed?.Invoke();
    }
    private static double Finite(double v, double min, double max, double fallback) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
    private static string SafeFont(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 120 && !value.Contains('/') && !value.Contains('\\') ? value : "Outfit";
    private void Commit(ThemePreferences next, bool rebuild = false)
    {
        if (_sync || next == _preferences) return;
        if (_undo.Count == 16) _undo.Dequeue(); _undo.Enqueue(_preferences); _preferences = next;
        OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(Snapshot));
        if (rebuild) RebuildEditor(); else NotifyPreview(); Changed?.Invoke();
    }
    private void ChangeTone(ThemeTone tone)
    {
        var next = EditTone == "深色" ? _preferences with { Dark = tone } : _preferences with { Light = tone };
        var pair = new ThemePair(next.Light ?? SelectedPreset.Light, next.Dark ?? SelectedPreset.Dark);
        if (next.SelectedCustomId is { } id)
            next = next with { Library = Library.Select(t => t.Id == id ? t with { Light = pair.Light, Dark = pair.Dark } : t).ToArray() };
        else
        { var overrides = new Dictionary<string, ThemePair>(next.Overrides ?? new Dictionary<string, ThemePair>()); overrides[SelectedPreset.Id] = pair; next = next with { Overrides = overrides }; }
        Commit(next);
    }
    private void SetColor(string key, string value)
    { var tone = Editing; if (tone.Colors[key] == value) return; var colors = new Dictionary<string, string>(tone.Colors) { [key] = value }; ChangeTone(tone with { Colors = colors }); }
    private void SetNumber(string key, double value)
    { var tone = Editing; if (tone.Numbers[key] == value) return; var numbers = new Dictionary<string, double>(tone.Numbers) { [key] = value }; ChangeTone(tone with { Numbers = numbers }); }
    private void RebuildEditor()
    {
        var tone = Editing; var fallback = EditTone == "深色" ? SelectedPreset.Dark : SelectedPreset.Light;
        ColorGroups = ThemeCatalog.Colors.GroupBy(d => d.Group).Select(g => new ThemeColorGroup(g.Key,
            g.Select(d => new ThemeColorField(d, tone.Colors[d.Key], fallback.Colors[d.Key], SetColor)).ToArray())).ToArray();
        NumberFields = ThemeCatalog.Numbers.Select(d => new ThemeNumberField(d, tone.Numbers[d.Key], SetNumber)).ToArray();
        _sync = true; MotionEnabled = tone.MotionEnabled; _sync = false; NotifyPreview();
        RefreshEditorState();
    }
    private void NotifyPreview()
    { foreach (var p in new[] { nameof(Editing), nameof(HasLibrary), nameof(CurrentThemeLabel), nameof(ContrastStatus), nameof(SelectedWallpaperEffect) }) OnPropertyChanged(p); }
    partial void OnEditToneChanged(string value) { RebuildEditor(); RefreshTextOverride(); }
    partial void OnSelectedPresetChanged(ThemePreset value)
    {
        if (_sync) return;
        var saved = _preferences.Overrides?.GetValueOrDefault(value.Id);
        Commit(_preferences with { Preset = value.Id, SelectedCustomId = null, Light = saved?.Light ?? value.Light, Dark = saved?.Dark ?? value.Dark }, true);
    }
    partial void OnModeChanged(string value) => Commit(_preferences with { Mode = value switch { "深色" => "dark", "跟随系统" => "system", "氛围" => "ambient", "亚克力" => "acrylic", _ => "light" } });
    partial void OnScheduleEnabledChanged(bool value) => Commit(_preferences with { ScheduleEnabled = value });
    partial void OnDarkAtChanged(string value) { if (TimeOnly.TryParseExact(value, "HH:mm", out _)) { Feedback = ""; Commit(_preferences with { DarkAt = value }); } else Feedback = "切换时间使用 24 小时制，例如 19:00"; }
    partial void OnLightAtChanged(string value) { if (TimeOnly.TryParseExact(value, "HH:mm", out _)) { Feedback = ""; Commit(_preferences with { LightAt = value }); } else Feedback = "切换时间使用 24 小时制，例如 07:00"; }
    partial void OnInvertChanged(bool value) => Commit(_preferences with { Invert = value });
    partial void OnFontChanged(string value) => Commit(_preferences with { Font = SafeFont(value) });
    partial void OnChineseFontChanged(string value) => Commit(_preferences with { ChineseFont = SafeFont(value) });
    partial void OnFallbackFontChanged(string value) => Commit(_preferences with { FallbackFont = SafeFont(value) });
    partial void OnWallpaperChanged(string value) { Commit(_preferences with { Wallpaper = value }); OnPropertyChanged(nameof(WallpaperFileName)); OnPropertyChanged(nameof(HasWallpaperFile)); }
    partial void OnWallpaperOpacityChanged(double value) => Commit(_preferences with { WallpaperOpacity = Math.Clamp(value, 0, 100) });
    partial void OnWallpaperBlurChanged(double value) => Commit(_preferences with { WallpaperBlur = Math.Clamp(value, 0, 40) });
    partial void OnWallpaperXChanged(double value) => Commit(_preferences with { WallpaperX = Math.Clamp(value, 0, 100) });
    partial void OnWallpaperYChanged(double value) => Commit(_preferences with { WallpaperY = Math.Clamp(value, 0, 100) });
    partial void OnWallpaperFitChanged(string value) { Commit(_preferences with { WallpaperFit = value }); OnPropertyChanged(nameof(SelectedWallpaperChoice)); }
    partial void OnMotionEnabledChanged(bool value) { if (!_sync) ChangeTone(Editing with { MotionEnabled = value }); }
    partial void OnAcrylicEnabledChanged(bool value) => Commit(_preferences with { AcrylicEnabled = value });
    partial void OnAcrylicTransparencyChanged(double value) => Commit(_preferences with { AcrylicTransparency = Finite(value, 0, 100, 70) });
    partial void OnAcrylicKeepUnfocusedChanged(bool value) => Commit(_preferences with { AcrylicKeepUnfocused = value });
    [RelayCommand] private void Undo()
    {
        if (_undo.Count == 0) return;
        var items = _undo.ToArray(); _undo.Clear(); foreach (var item in items.SkipLast(1)) _undo.Enqueue(item);
        Restore(items[^1]); Feedback = "已撤销上一次修改"; OnPropertyChanged(nameof(CanUndo));
    }
    [RelayCommand] private void ResetTone() { ChangeTone(EditTone == "深色" ? SelectedPreset.Dark : SelectedPreset.Light); RebuildEditor(); }
    [RelayCommand] private void ResetAll() { Commit(new ThemePreferences(), true); Restore(_preferences); Feedback = "已恢复经典外观，可撤销"; }
    [RelayCommand] private void ClearWallpaper() => Wallpaper = "";
    public static bool ScheduledDark(TimeOnly now, TimeOnly darkAt, TimeOnly lightAt) => darkAt == lightAt ? false
        : darkAt < lightAt ? now >= darkAt && now < lightAt : now >= darkAt || now < lightAt;
}
