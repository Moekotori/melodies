using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Echo.Desktop.Theming;
using System.ComponentModel;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Settings;

public partial class ThemeSettingsView : UserControl
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    private ThemeViewModel? _observed;
    public ThemeSettingsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ObserveEditor();
        DetachedFromVisualTree += (_, _) =>
        {
            if (_observed is not null) { _observed.IsPresetGalleryOpen = false; _observed.PropertyChanged -= EditorChanged; }
            _observed = null; CloseSelections(); _lifetime.Cancel();
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && _observed?.IsPresetGalleryOpen == true)
            { _observed.IsPresetGalleryOpen = false; args.Handled = true; }
        };
        SizeChanged += (_, _) =>
        {
            var available = Math.Max(140, Bounds.Width);
            var columns = available >= 760 ? 3 : available >= 460 ? 2 : 1;
            Resources["ThemeColorFieldWidth"] = available / columns - 16;
            Resources["ThemeModeWidth"] = Math.Min(500, available);
        };
    }
    private void ObserveEditor()
    {
        if (_observed is not null) _observed.PropertyChanged -= EditorChanged;
        _observed = DataContext as ThemeViewModel;
        if (_observed is not null) _observed.PropertyChanged += EditorChanged;
    }
    private void EditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ThemeViewModel.EditorSection)) CloseSelections();
    }
    private void CloseSelections()
    { foreach (var select in this.GetVisualDescendants().OfType<ComboBox>()) select.IsDropDownOpen = false; }
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
}
