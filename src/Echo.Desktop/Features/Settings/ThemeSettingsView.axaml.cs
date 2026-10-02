using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Echo.Desktop.Theming;

namespace Echo.Desktop.Features.Settings;

public partial class ThemeSettingsView : UserControl
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    public ThemeSettingsView()
    { InitializeComponent(); DetachedFromVisualTree += (_, _) => _lifetime.Cancel(); }
    private async void ImportTheme(object? sender, RoutedEventArgs args)
    {
        if (_busy || DataContext is not ThemeViewModel model || TopLevel.GetTopLevel(this) is not { } top) return;
        _busy = true;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = "导入 ECHO 主题", AllowMultiple = false,
                FileTypeFilter = [new("ECHO 主题") { Patterns = ["*.json"] }] });
            if (files.Count == 0 || _lifetime.IsCancellationRequested) return;
            await using var stream = await files[0].OpenReadAsync();
            var bytes = new byte[65537]; var count = 0;
            while (count < bytes.Length)
            { var read = await stream.ReadAsync(bytes.AsMemory(count), _lifetime.Token); if (read == 0) break; count += read; }
            if (count > 65536) throw new InvalidDataException("主题文件最大 64 KiB");
            if (!_lifetime.IsCancellationRequested) model.ImportJson(Encoding.UTF8.GetString(bytes, 0, count));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { if (!_lifetime.IsCancellationRequested) model.Feedback = error is InvalidDataException ? error.Message : "主题导入失败，请检查文件内容"; }
        finally { _busy = false; }
    }
    private async void ExportTheme(object? sender, RoutedEventArgs args)
    {
        if (_busy || DataContext is not ThemeViewModel model || TopLevel.GetTopLevel(this) is not { } top) return;
        _busy = true;
        try
        {
            var json = model.ExportJson();
            var file = await top.StorageProvider.SaveFilePickerAsync(new() { Title = "导出 ECHO 主题", SuggestedFileName = "echo-theme.json", DefaultExtension = "json",
                FileTypeChoices = [new("ECHO 主题") { Patterns = ["*.json"] }] });
            if (file is null || _lifetime.IsCancellationRequested) return;
            await using var stream = await file.OpenWriteAsync(); if (stream.CanSeek) stream.SetLength(0);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(json), _lifetime.Token); model.Feedback = "主题已导出";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { model.Feedback = "主题导出失败，请选择其他保存位置"; }
        finally { _busy = false; }
    }
    private async void ChooseWallpaper(object? sender, RoutedEventArgs args)
    {
        if (_busy || DataContext is not ThemeViewModel model || TopLevel.GetTopLevel(this) is not { } top) return;
        _busy = true;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = "选择背景图片", FileTypeFilter = [FilePickerFileTypes.ImageAll] });
            if (files.Count == 0 || _lifetime.IsCancellationRequested) return;
            if (files[0].TryGetLocalPath() is { } path) model.Wallpaper = path;
            else model.Feedback = "请选择本地图片";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { model.Feedback = "图片选择失败，请重试"; }
        finally { _busy = false; }
    }
}
