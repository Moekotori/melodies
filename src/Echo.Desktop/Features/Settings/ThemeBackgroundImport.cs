using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Echo.Desktop.Theming;
using Echo.Infrastructure.Media;
using Echo.Wallpapers;

namespace Echo.Desktop.Features.Settings;

public partial class ThemeBackgroundSettings
{
    private CancellationTokenSource? _importCancellation;
    private WallpaperEnginePicker? _wallpaperPicker;
    private readonly ImportedWallpaperStore _wallpapers = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "wallpapers"));
    private static readonly FilePickerFileType BackgroundFiles = new("图片和视频")
    { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.tif", "*.tiff", "*.gif", "*.mp4", "*.m4v", "*.webm", "*.mkv", "*.mov", "*.avi", "*.wmv"] };

    private async void ChooseWallpaperEngine(object? sender, RoutedEventArgs args)
        => await ChooseWallpaperEngineMedia(false);
    private async void ChoosePortraitWallpaperEngine(object? sender, RoutedEventArgs args)
        => await ChooseWallpaperEngineMedia(true);
    private async Task ChooseWallpaperEngineMedia(bool portrait)
    {
        if (_busy || DataContext is not ThemeViewModel model || TopLevel.GetTopLevel(this) is not Window owner) return;
        _busy = true;
        try
        {
            var dark = owner.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            _wallpaperPicker = new WallpaperEnginePicker { RequestedThemeVariant = owner.ActualThemeVariant,
                Resources = ThemeResourcePalette.Create(model.Tone(dark), model.Snapshot, dark, null, false, false) };
            var project = await _wallpaperPicker.ShowDialog<WallpaperEngineProject?>(owner);
            if (_detached || !ReferenceEquals(DataContext, model) || project?.Supported != true) return;
            // Re-read metadata after selection; a Workshop update may have replaced the project.
            var current = await Task.Run(() => WallpaperEngineProject.Read(project.Folder));
            if (!current.Supported || current.MediaPath != project.MediaPath || current.EntryName != project.EntryName || current.Kind != project.Kind)
                throw new InvalidDataException("壁纸项目已变化，请重新选择");
            if (current.Kind is "scene" or "web") await ImportProject(current, portrait, model);
            else await ImportMedia(current.MediaPath!, portrait, model);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        { if (!_detached) model.Feedback = error is InvalidDataException ? error.Message : "壁纸导入失败，请重新选择"; }
        finally { _wallpaperPicker = null; _busy = false; }
    }

    private async Task ImportProject(WallpaperEngineProject project, bool portrait, ThemeViewModel model)
    {
        if (_detached || !ReferenceEquals(DataContext, model)) return;
        using var cancellation = _importCancellation = new();
        model.Feedback = "正在导入背景…";
        try
        {
            var saved = await Task.Run(() => WallpaperProjectImporter.ImportAsync(project.Folder,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "wallpapers"),
                project.Kind, project.EntryName!, cancellation.Token), cancellation.Token);
            if (_detached || cancellation.IsCancellationRequested || !ReferenceEquals(DataContext, model)) return;
            model.SelectWallpaper(saved, portrait); model.Feedback = "";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { model.Feedback = ""; }
        finally { if (ReferenceEquals(_importCancellation, cancellation)) _importCancellation = null; }
    }

    private async Task ImportMedia(string path, bool portrait, ThemeViewModel model)
    {
        if (_detached || !ReferenceEquals(DataContext, model)) return;
        _importCancellation?.Dispose();
        using var cancellation = _importCancellation = new();
        model.Feedback = "正在导入背景…";
        try
        {
            var saved = await Task.Run(async () =>
            {
                await WallpaperImportValidation.ValidateAsync(path, cancellation.Token);
                return await _wallpapers.ImportAsync(path, cancellation.Token);
            }, cancellation.Token);
            if (_detached || cancellation.IsCancellationRequested || !ReferenceEquals(DataContext, model)) return;
            model.SelectWallpaper(saved, portrait);
            model.Feedback = "";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { model.Feedback = ""; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or NotSupportedException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { if (!_detached) model.Feedback = error is InvalidDataException ? error.Message : "背景导入失败，请检查素材和媒体解码器"; }
        finally { if (ReferenceEquals(_importCancellation, cancellation)) _importCancellation = null; }
    }
}
