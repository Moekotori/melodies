namespace Echo.Contracts.Settings;

public sealed record ImportedFont(string Path, string Family);
public sealed record WallpaperFraming(string Fit = "UniformToFill", double X = 50, double Y = 50, double Zoom = 100);
public sealed record AppearancePreferences(
    bool WallpaperEnabled = true, string PortraitWallpaper = "", WallpaperFraming? PortraitFraming = null,
    double WallpaperZoom = 100, double WallpaperBrightness = 100, double WallpaperUiOpacity = 100,
    bool WallpaperUnifiedOpacity = true, bool WallpaperVisualProtection = true, string VideoPauseMode = "smart",
    ImportedFont? MainFontFile = null, ImportedFont? ChineseFontFile = null, ImportedFont? FallbackFontFile = null,
    string? PageTextLight = null, string? PageTextDark = null, string CoverShape = "square", bool ArtistAlbumArtwork = false,
    bool RainbowEnabled = false, string RainbowStyle = "aurora", double RainbowSpeed = 100, double RainbowIntensity = 100);
