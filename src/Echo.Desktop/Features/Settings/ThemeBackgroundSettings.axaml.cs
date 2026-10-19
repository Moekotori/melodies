using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Echo.Desktop.Theming;

namespace Echo.Desktop.Features.Settings;

public partial class ThemeBackgroundSettings : UserControl
{
    private bool _busy;
    private bool _detached;
    private Window? _previewWindow;
    public ThemeBackgroundSettings()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => _detached = false;
        DetachedFromVisualTree += (_, _) => { _detached = true; _previewWindow?.Close(); _wallpaperPicker?.Close(); _importCancellation?.Cancel(); };
        PropertyChanged += (_, args) => { if (args.Property == IsVisibleProperty && !IsVisible) _previewWindow?.Close(); };
        SizeChanged += (_, _) => Reflow();
        WindowPreview.ExpandRequested += OpenPreview;
    }
    private void Reflow()
    {
        var narrow = Bounds.Width < 790;
        WallpaperWorkspace.ColumnDefinitions = new(narrow ? "*" : "*,312");
        Grid.SetRow(AdjustmentColumn, narrow ? 1 : 0); Grid.SetColumn(AdjustmentColumn, narrow ? 0 : 1);
    }
    private void OpenPreview()
    {
        if (DataContext is not ThemeViewModel model || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (_previewWindow is not null) { _previewWindow.Activate(); return; }
        var scene = new ThemeWallpaperPreview { DataContext = model, Margin = new(24) }; scene.SetTone(WindowPreview.IsDarkPreview);
        scene.FindControl<Button>("ExpandPreview")!.Content = "关闭预览";
        Avalonia.Automation.AutomationProperties.SetName(scene.FindControl<Button>("ExpandPreview")!, "关闭背景预览");
        var dark = owner.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var palette = ThemeResourcePalette.Create(model.Tone(dark), model.Snapshot, dark, null, false, false);
        var window = new Window { Title = "背景预览", Width = 1080, Height = 760, MinWidth = 640, MinHeight = 480,
            Content = scene, Resources = palette, RequestedThemeVariant = owner.ActualThemeVariant,
            Background = (Avalonia.Media.IBrush)palette["EchoPage"]!, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        scene.ExpandRequested += window.Close;
        var frame = scene.FindControl<Viewbox>("SceneViewbox")!;
        window.SizeChanged += (_, _) => frame.MaxHeight = Math.Max(240, window.ClientSize.Height - 105);
        _previewWindow = window; window.Closed += (_, _) => { scene.ExpandRequested -= window.Close; _previewWindow = null; };
        window.Show(owner);
    }
    private async void ChooseImage(object? sender, RoutedEventArgs args)
        => await ChooseMedia(false);
    private async void ChoosePortrait(object? sender, RoutedEventArgs args)
        => await ChooseMedia(true);
    private async Task ChooseMedia(bool portrait)
    {
        if (_busy || DataContext is not ThemeViewModel model || TopLevel.GetTopLevel(this) is not { } top) return;
        _busy = true;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = portrait ? "选择竖屏背景" : "选择背景",
                AllowMultiple = false, FileTypeFilter = [BackgroundFiles, FilePickerFileTypes.ImageAll,
                    new FilePickerFileType("视频") { Patterns = ["*.mp4", "*.m4v", "*.webm", "*.mkv", "*.mov", "*.avi", "*.wmv", "*.gif"] }] });
            if (_detached || !ReferenceEquals(DataContext, model) || files.Count == 0) return;
            if (files[0].TryGetLocalPath() is { } path) await ImportMedia(path, portrait, model);
            else model.Feedback = "请选择本地图片或视频";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { if (!_detached) model.Feedback = error is InvalidDataException ? error.Message : "背景导入失败，请重试"; }
        finally { _busy = false; }
    }
}
