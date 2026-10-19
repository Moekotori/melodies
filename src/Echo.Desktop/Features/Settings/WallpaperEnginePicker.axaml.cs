using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Echo.Desktop.Features.Settings;

public partial class WallpaperEnginePicker : Window
{
    private readonly WallpaperEnginePickerModel _model = new();
    public WallpaperEnginePicker()
    {
        InitializeComponent(); DataContext = _model;
        Opened += async (_, _) => await _model.ScanAsync();
        Closed += (_, _) => _model.Dispose();
    }
    private async void ChooseFolder(object? sender, RoutedEventArgs args)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "选择 Steam 曲库、Wallpaper Engine 或壁纸项目目录", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) await _model.ScanAsync(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { _model.Status = "目录读取失败，请重试"; }
    }
    private void Cancel(object? sender, RoutedEventArgs args) => Close();
    private void Select(object? sender, RoutedEventArgs args)
    { if (_model.CanSelect) Close(_model.Selected); }
}
