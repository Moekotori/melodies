using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Theming;

// Additional legacy controls share the theme snapshot, undo stack and single settings writer.
public sealed partial class ThemeViewModel
{
    public Func<string, string, CancellationToken, Task>? ImportFontAsync { get; set; }
    public AppearancePreferences Appearance => _preferences.Appearance ?? new();
    public IReadOnlyList<WallpaperFitChoice> RainbowChoices { get; } = [new("aurora", "极光"), new("ribbon", "丝带"),
        new("wheel", "色轮"), new("nebula", "星云"), new("prism", "棱镜"), new("tide", "潮汐"), new("breathe", "呼吸"), new("accents", "强调色")];
    public IReadOnlyList<WallpaperFitChoice> VideoPauseChoices { get; } = [new("smart", "智能暂停"), new("minimized", "最小化暂停"), new("never", "持续播放")];
    [ObservableProperty] private bool wallpaperEnabled = true;
    [ObservableProperty] private string portraitWallpaper = "";
    [ObservableProperty] private double wallpaperZoom = 100;
    [ObservableProperty] private double wallpaperBrightness = 100;
    [ObservableProperty] private double wallpaperUiOpacity = 100;
    [ObservableProperty] private bool wallpaperUnifiedOpacity = true;
    [ObservableProperty] private bool wallpaperVisualProtection = true;
    [ObservableProperty] private string videoPauseMode = "smart";
    [ObservableProperty] private bool rainbowEnabled;
    [ObservableProperty] private string rainbowStyle = "aurora";
    [ObservableProperty] private double rainbowSpeed = 100;
    [ObservableProperty] private double rainbowIntensity = 100;
    [ObservableProperty] private string coverShape = "square";
    [ObservableProperty] private bool artistAlbumArtwork;
    [ObservableProperty] private string pageTextColor = "";
    [ObservableProperty] private string fontStatus = "";
    [ObservableProperty] private IReadOnlyList<string> availableFonts = [];
    [ObservableProperty] private double portraitX = 50;
    [ObservableProperty] private double portraitY = 50;
    [ObservableProperty] private double portraitZoom = 100;
    [ObservableProperty] private string portraitFit = "UniformToFill";
    public WallpaperFitChoice SelectedRainbowChoice { get => RainbowChoices.First(c => c.Code == RainbowStyle); set { if (value is not null) RainbowStyle = value.Code; } }
    public WallpaperFitChoice SelectedVideoPauseChoice { get => VideoPauseChoices.First(c => c.Code == VideoPauseMode); set { if (value is not null) VideoPauseMode = value.Code; } }
    public WallpaperFitChoice SelectedPortraitFit { get => WallpaperChoices.First(c => c.Code == PortraitFit); set { if (value is not null) PortraitFit = value.Code; } }
    public bool RoundedCovers { get => CoverShape == "rounded"; set => CoverShape = value ? "rounded" : "square"; }
    public bool HasPageTextOverride => (EditTone == "深色" ? Appearance.PageTextDark : Appearance.PageTextLight) is not null;
    private void ChangeAppearance(AppearancePreferences next) { if (!_sync) Commit(_preferences with { Appearance = NormalizeAppearance(next) }); }
    private static string LocalPath(string? path) => path?.Length <= 1024 && !path.Contains('\n') && !path.Contains('\r') ? path : "";
    private static AppearancePreferences NormalizeAppearance(AppearancePreferences a) => a with
    {
        PortraitWallpaper = LocalPath(a.PortraitWallpaper), WallpaperZoom = Finite(a.WallpaperZoom, 25, 400, 100),
        WallpaperBrightness = Finite(a.WallpaperBrightness, 0, 200, 100), WallpaperUiOpacity = Finite(a.WallpaperUiOpacity, 0, 100, 100),
        VideoPauseMode = a.VideoPauseMode is "minimized" or "never" ? a.VideoPauseMode : "smart",
        CoverShape = a.CoverShape == "rounded" ? "rounded" : "square",
        RainbowStyle = new[] { "aurora", "ribbon", "wheel", "nebula", "prism", "tide", "breathe", "accents" }.Contains(a.RainbowStyle) ? a.RainbowStyle : "aurora",
        RainbowSpeed = Finite(a.RainbowSpeed, 25, 400, 100), RainbowIntensity = Finite(a.RainbowIntensity, 20, 200, 100),
        PageTextLight = ThemeColorMath.TryHex(a.PageTextLight ?? "", out var light) ? light : null,
        PageTextDark = ThemeColorMath.TryHex(a.PageTextDark ?? "", out var dark) ? dark : null,
        PortraitFraming = NormalizeFraming(a.PortraitFraming ?? new()),
        MainFontFile = NormalizeFont(a.MainFontFile), ChineseFontFile = NormalizeFont(a.ChineseFontFile), FallbackFontFile = NormalizeFont(a.FallbackFontFile)
    };
    private static ImportedFont? NormalizeFont(ImportedFont? file) => file is not null && LocalPath(file.Path).Length > 0
        && !string.IsNullOrWhiteSpace(file.Family) && file.Family.Length <= 120 ? file : null;
    private static WallpaperFraming NormalizeFraming(WallpaperFraming f) => new(
        new[] { "UniformToFill", "Uniform", "Fill", "Tile", "Center" }.Contains(f.Fit) ? f.Fit : "UniformToFill",
        Finite(f.X, 0, 100, 50), Finite(f.Y, 0, 100, 50), Finite(f.Zoom, 25, 400, 100));
    private void RestoreAppearance()
    {
        var a = NormalizeAppearance(Appearance); _preferences = _preferences with { Appearance = a };
        WallpaperEnabled = a.WallpaperEnabled; PortraitWallpaper = a.PortraitWallpaper;
        WallpaperZoom = a.WallpaperZoom; WallpaperBrightness = a.WallpaperBrightness; WallpaperUiOpacity = a.WallpaperUiOpacity;
        WallpaperUnifiedOpacity = a.WallpaperUnifiedOpacity; WallpaperVisualProtection = a.WallpaperVisualProtection; VideoPauseMode = a.VideoPauseMode;
        RainbowEnabled = a.RainbowEnabled; RainbowStyle = a.RainbowStyle; RainbowSpeed = a.RainbowSpeed; RainbowIntensity = a.RainbowIntensity;
        CoverShape = a.CoverShape; ArtistAlbumArtwork = a.ArtistAlbumArtwork;
        var f = a.PortraitFraming!; PortraitFit = f.Fit; PortraitX = f.X; PortraitY = f.Y; PortraitZoom = f.Zoom;
        RefreshTextOverride();
        foreach (var p in new[] { nameof(RoundedCovers), nameof(SelectedRainbowChoice), nameof(SelectedVideoPauseChoice), nameof(SelectedPortraitFit), nameof(Appearance) }) OnPropertyChanged(p);
    }
    private void RefreshTextOverride()
    { var wasSync = _sync; _sync = true; PageTextColor = (EditTone == "深色" ? Appearance.PageTextDark : Appearance.PageTextLight) ?? ""; _sync = wasSync; OnPropertyChanged(nameof(HasPageTextOverride)); }
    partial void OnWallpaperEnabledChanged(bool value) => ChangeAppearance(Appearance with { WallpaperEnabled = value });
    partial void OnPortraitWallpaperChanged(string value) { ChangeAppearance(Appearance with { PortraitWallpaper = value }); OnPropertyChanged(nameof(PortraitFileName)); }
    partial void OnWallpaperZoomChanged(double value) => ChangeAppearance(Appearance with { WallpaperZoom = value });
    partial void OnWallpaperBrightnessChanged(double value) => ChangeAppearance(Appearance with { WallpaperBrightness = value });
    partial void OnWallpaperUiOpacityChanged(double value) => ChangeAppearance(Appearance with { WallpaperUiOpacity = value });
    partial void OnWallpaperUnifiedOpacityChanged(bool value) => ChangeAppearance(Appearance with { WallpaperUnifiedOpacity = value });
    partial void OnWallpaperVisualProtectionChanged(bool value) => ChangeAppearance(Appearance with { WallpaperVisualProtection = value });
    partial void OnVideoPauseModeChanged(string value) { ChangeAppearance(Appearance with { VideoPauseMode = value }); OnPropertyChanged(nameof(SelectedVideoPauseChoice)); }
    partial void OnRainbowEnabledChanged(bool value) => ChangeAppearance(Appearance with { RainbowEnabled = value });
    partial void OnRainbowStyleChanged(string value) { ChangeAppearance(Appearance with { RainbowStyle = value }); OnPropertyChanged(nameof(SelectedRainbowChoice)); }
    partial void OnRainbowSpeedChanged(double value) => ChangeAppearance(Appearance with { RainbowSpeed = value });
    partial void OnRainbowIntensityChanged(double value) => ChangeAppearance(Appearance with { RainbowIntensity = value });
    partial void OnCoverShapeChanged(string value) { ChangeAppearance(Appearance with { CoverShape = value }); OnPropertyChanged(nameof(RoundedCovers)); }
    partial void OnArtistAlbumArtworkChanged(bool value) => ChangeAppearance(Appearance with { ArtistAlbumArtwork = value });
    partial void OnPageTextColorChanged(string value)
    {
        if (_sync) return;
        if (value.Length > 0 && !ThemeColorMath.TryHex(value, out _)) { Feedback = "文字颜色使用 HEX 或 RGBA，例如 #334155"; return; }
        ChangeAppearance(EditTone == "深色" ? Appearance with { PageTextDark = value.Length == 0 ? null : value } : Appearance with { PageTextLight = value.Length == 0 ? null : value });
        Feedback = ""; OnPropertyChanged(nameof(HasPageTextOverride));
    }
    private void ChangePortrait() => ChangeAppearance(Appearance with { PortraitFraming = new(PortraitFit, PortraitX, PortraitY, PortraitZoom) });
    partial void OnPortraitXChanged(double value) => ChangePortrait();
    partial void OnPortraitYChanged(double value) => ChangePortrait();
    partial void OnPortraitZoomChanged(double value) => ChangePortrait();
    partial void OnPortraitFitChanged(string value) { ChangePortrait(); OnPropertyChanged(nameof(SelectedPortraitFit)); }
    public void SetFontFile(string slot, ImportedFont? file)
    {
        var appearance = slot switch { "chinese" => Appearance with { ChineseFontFile = file }, "fallback" => Appearance with { FallbackFontFile = file }, _ => Appearance with { MainFontFile = file } };
        var next = _preferences with { Appearance = NormalizeAppearance(appearance) };
        if (file is not null) next = slot switch { "chinese" => next with { ChineseFont = file.Family }, "fallback" => next with { FallbackFont = file.Family }, _ => next with { Font = file.Family } };
        Commit(next); Restore(next);
    }
    [RelayCommand] private void ClearPortraitWallpaper() => PortraitWallpaper = "";
    [RelayCommand] private void ResetPageText() => PageTextColor = "";
    [RelayCommand] private void ResetWallpaperPosition() { WallpaperX = WallpaperY = 50; WallpaperZoom = 100; PortraitX = PortraitY = 50; PortraitZoom = 100; }
}
