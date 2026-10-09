using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Echo.Desktop.Theming;

namespace Echo.Desktop.Features.Settings;

// A scaled window scene uses the same palette and wallpaper renderer as the main window.
// It borrows the window bitmap; no additional decoder, file access, timer or bitmap ownership.
public partial class ThemeWallpaperPreview : UserControl
{
    private ThemeViewModel? _model;
    private bool _dark;
    private bool _attached;
    public bool IsDarkPreview => _dark;
    public event Action? ExpandRequested;
    public ThemeWallpaperPreview()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { _attached = true; Observe(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; Release(); };
        DataContextChanged += (_, _) => { if (_attached) Observe(); };
    }
    private void Observe()
    {
        Release(); _model = DataContext as ThemeViewModel;
        if (_model is null) return;
        _model.PropertyChanged += ModelChanged; _model.Changed += Refresh;
        Refresh();
    }
    private void Release()
    {
        if (_model is not null) { _model.PropertyChanged -= ModelChanged; _model.Changed -= Refresh; }
        _model = null;
        PreviewWallpaper.Image = null;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ThemeViewModel.WallpaperPreview) or nameof(ThemeViewModel.EditorSection) or nameof(ThemeViewModel.IsPortraitWallpaperActive)) Refresh();
        else if (args.PropertyName == nameof(ThemeViewModel.WallpaperFrameVersion) && _model?.IsBackgroundSection == true)
            PreviewWallpaper.ContentRevision = _model.WallpaperFrameVersion;
    }
    private void PreviewLight(object? sender, RoutedEventArgs args) => SetTone(false);
    private void PreviewDark(object? sender, RoutedEventArgs args) => SetTone(true);
    public void SetTone(bool dark) { _dark = dark; Refresh(); }
    private void Expand(object? sender, RoutedEventArgs args) => ExpandRequested?.Invoke();
    private void Refresh()
    {
        if (_model is not { } model) return;
        if (!model.IsBackgroundSection) { PreviewWallpaper.Image = null; return; }
        var p = model.Snapshot; var a = model.Appearance;
        var portrait = model.IsPortraitWallpaperActive;
        PreviewScene.Width = portrait ? 800 : 960; PreviewScene.Height = portrait ? 1000 : 600;
        var framing = portrait ? a.PortraitFraming ?? new() : new Echo.Contracts.Settings.WallpaperFraming(p.WallpaperFit, p.WallpaperX, p.WallpaperY, a.WallpaperZoom);
        PreviewTone.RequestedThemeVariant = _dark ? ThemeVariant.Dark : ThemeVariant.Light;
        PreviewTone.Resources = ThemeResourcePalette.Create(model.Tone(_dark), p, _dark, null, false, model.WallpaperPreview is not null);
        PreviewWallpaper.Image = model.WallpaperPreview;
        PreviewWallpaper.Fit = framing.Fit; PreviewWallpaper.PositionX = framing.X / 100; PreviewWallpaper.PositionY = framing.Y / 100;
        PreviewWallpaper.Zoom = framing.Fit is "Uniform" or "UniformToFill" ? framing.Zoom / 100 : 1;
        PreviewWallpaper.Blur = p.WallpaperBlur; PreviewWallpaper.Opacity = p.WallpaperOpacity / 100;
        PreviewWallpaper.ContentRevision = model.WallpaperFrameVersion;
        PreviewWallpaper.InvalidateVisual(); EmptyPreview.IsVisible = model.WallpaperPreview is null;
        LightPreview.Classes.Set("selected", !_dark); DarkPreview.Classes.Set("selected", _dark);
    }
}
