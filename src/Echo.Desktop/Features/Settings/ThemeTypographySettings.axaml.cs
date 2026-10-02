using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Echo.Desktop.Theming;

namespace Echo.Desktop.Features.Settings;

public partial class ThemeTypographySettings : UserControl
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    public ThemeTypographySettings()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is not ThemeViewModel model) return;
            var fonts = await Task.Run(() => FontManager.Current.SystemFonts.Select(f => f.Name).Concat(model.Fonts).Distinct().OrderBy(f => f).Take(1024).ToArray());
            if (!_lifetime.IsCancellationRequested) model.AvailableFonts = fonts;
        };
        DetachedFromVisualTree += (_, _) => _lifetime.Cancel();
    }
    private async void ImportFont(object? sender, RoutedEventArgs args)
    {
        if (_busy || sender is not Button { Tag: string slot } || DataContext is not ThemeViewModel model || TopLevel.GetTopLevel(this) is not { } top) return;
        if (model.ImportFontAsync is not { } import) { model.FontStatus = "字体导入服务尚未就绪"; return; }
        _busy = true;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = "导入字体", FileTypeFilter = [new("字体") { Patterns = ["*.ttf", "*.otf", "*.ttc"] }] });
            if (_lifetime.IsCancellationRequested || files.Count == 0) return;
            if (files[0].TryGetLocalPath() is not { } path) { model.FontStatus = "请选择本地字体文件"; return; }
            await import(slot, path, _lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { if (!_lifetime.IsCancellationRequested) model.FontStatus = error is InvalidDataException ? error.Message : "字体导入失败，请重新选择"; }
        finally { _busy = false; }
    }
    private void ClearFont(object? sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string slot } && DataContext is ThemeViewModel model) model.SetFontFile(slot, null); }
}
