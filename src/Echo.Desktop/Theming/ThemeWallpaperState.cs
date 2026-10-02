using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Theming;

// The window decoder owns the bitmap; the editor only borrows it for its preview.
public sealed partial class ThemeViewModel
{
    public event Action? WallpaperReloadRequested;
    public string WallpaperFileName => string.IsNullOrEmpty(Wallpaper) ? "未选择背景" : Path.GetFileName(Wallpaper);
    public string PortraitFileName => string.IsNullOrEmpty(PortraitWallpaper) ? "沿用主背景" : Path.GetFileName(PortraitWallpaper);
    public bool HasWallpaperFile => Wallpaper.Length > 0;
    [ObservableProperty] private Bitmap? wallpaperPreview;
    [ObservableProperty] private int wallpaperFrameVersion;
    [ObservableProperty] private bool isPortraitWallpaperActive;
    [ObservableProperty] private string wallpaperStatus = "选择本地图片或视频，立即应用到整个窗口";
    public IReadOnlyList<WallpaperEffectChoice> WallpaperEffects { get; } =
    [
        new("均衡", 100, 0, 92, 76), new("柔和", 106, 8, 84, 70),
        new("沉浸", 102, 2, 90, 56), new("轻盈", 100, 0, 106, 64),
        new("鲜明", 104, 0, 118, 30), new("梦幻", 110, 14, 112, 45),
        new("电影", 108, 4, 72, 48), new("黑白氛围", 105, 1, 52, 60, false),
        new("舞台", 114, 0, 82, 20, false), new("专注", 112, 16, 78, 82),
        new("全透明", 100, 0, 100, 0, true, false)
    ];
    public WallpaperEffectChoice? SelectedWallpaperEffect
    {
        get => WallpaperEffects.FirstOrDefault(e => e.Zoom == WallpaperZoom && e.Blur == WallpaperBlur
            && e.Brightness == WallpaperBrightness && e.UiOpacity == WallpaperUiOpacity
            && e.Unified == WallpaperUnifiedOpacity && (!e.Protection.HasValue || e.Protection == WallpaperVisualProtection));
        set { if (value is not null) ApplyWallpaperEffect(value); }
    }
    private void ApplyWallpaperEffect(WallpaperEffectChoice effect)
    {
        var next = _preferences with { WallpaperOpacity = 100, WallpaperBlur = effect.Blur,
            Appearance = Appearance with { WallpaperZoom = effect.Zoom, WallpaperBrightness = effect.Brightness,
                WallpaperUiOpacity = effect.UiOpacity, WallpaperUnifiedOpacity = effect.Unified,
                WallpaperVisualProtection = effect.Protection ?? WallpaperVisualProtection } };
        Commit(next); Restore(next);
        OnPropertyChanged(nameof(SelectedWallpaperEffect));
    }
    public void SelectWallpaper(string path, bool portrait)
    {
        var same = path == (portrait ? PortraitWallpaper : Wallpaper);
        var first = string.IsNullOrEmpty(Wallpaper) && string.IsNullOrEmpty(PortraitWallpaper);
        if (first) ApplyWallpaperEffect(WallpaperEffects[0]);
        WallpaperEnabled = true;
        if (portrait) PortraitWallpaper = path; else Wallpaper = path;
        if (same) WallpaperReloadRequested?.Invoke();
    }
    [RelayCommand] private void ResetWallpaperEffects() => ApplyWallpaperEffect(WallpaperEffects[0]);
}

public sealed record WallpaperEffectChoice(string Label, double Zoom, double Blur, double Brightness,
    double UiOpacity, bool Unified = true, bool? Protection = null)
{ public override string ToString() => Label; }
