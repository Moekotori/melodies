using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Infrastructure.Media;
using Echo.Platform.Steam;
using SkiaSharp;

namespace Echo.Desktop.Features.Settings;

public sealed partial class WallpaperEnginePickerModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _previewGate = new(1);
    private CancellationTokenSource? _previewCancellation;
    private IReadOnlyList<WallpaperEngineProject> _projects = [];
    private bool _disposed;
    [ObservableProperty] private string query = "";
    [ObservableProperty] private string status = "正在查找已安装的壁纸…";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private WallpaperEngineProject? selected;
    [ObservableProperty] private Bitmap? preview;
    [ObservableProperty] private IReadOnlyList<WallpaperEngineProject> visibleProjects = [];
    public bool CanScan => !Busy && !_disposed;
    public bool CanSelect => CanScan && Selected?.Supported == true;
    partial void OnBusyChanged(bool value) { OnPropertyChanged(nameof(CanScan)); OnPropertyChanged(nameof(CanSelect)); }
    partial void OnQueryChanged(string value) => Filter();
    partial void OnSelectedChanged(WallpaperEngineProject? value)
    {
        OnPropertyChanged(nameof(CanSelect));
        _previewCancellation?.Cancel(); _previewCancellation?.Dispose();
        _previewCancellation = null;
        var old = Preview; Preview = null; old?.Dispose();
        if (_disposed) return;
        _previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        if (value?.PreviewPath is { } path) _ = LoadPreview(path, _previewCancellation.Token);
    }
    private void Filter()
    {
        VisibleProjects = string.IsNullOrWhiteSpace(Query) ? _projects
            : _projects.Where(p => p.Title.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (Selected is not null && !VisibleProjects.Contains(Selected)) Selected = null;
    }
    public async Task ScanAsync(string? folder = null)
    {
        if (!CanScan) return;
        Busy = true; Selected = null; _projects = []; Filter(); Status = "正在查找已安装的壁纸…";
        var token = _stop.Token;
        try
        {
            var result = await Task.Run(() => WallpaperEngineLibrary.Scan(folder is null ? SteamLibraryLocations.Find(token) : [folder], token), token);
            if (_disposed) return;
            _projects = result.Projects.OrderByDescending(p => p.Supported).ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
            Filter();
            Status = _projects.Count == 0 ? "未找到壁纸，请选择 Steam 曲库或壁纸项目目录"
                : $"{_projects.Count} 个壁纸 · {_projects.Count(p => p.Supported)} 个可导入";
            if (result.Skipped > 0) Status += $" · 跳过 {result.Skipped} 个无效项目";
            if (result.Truncated) Status += " · 已达 2000 项上限，可选择更小的目录";
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        { if (!_disposed) Status = "壁纸库读取失败，请选择目录重试"; }
        finally { Busy = false; }
    }
    private async Task LoadPreview(string path, CancellationToken token)
    {
        Bitmap? image = null;
        try
        {
            await _previewGate.WaitAsync(token);
            try
            {
                image = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var file = File.OpenRead(path);
                    if (file.Length > 16 * 1024 * 1024) return null;
                    bool wide;
                    using (var codec = SKCodec.Create(file))
                    {
                        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || codec.Info.Width > 8192 || codec.Info.Height > 8192
                            || (long)codec.Info.Width * codec.Info.Height > 16 * 1024 * 1024) return null;
                        wide = codec.Info.Width >= codec.Info.Height;
                    }
                    using var source = File.OpenRead(path);
                    token.ThrowIfCancellationRequested();
                    return wide ? Bitmap.DecodeToWidth(source, 400) : Bitmap.DecodeToHeight(source, 400);
                }, token);
            }
            finally { _previewGate.Release(); }
            if (_disposed || token.IsCancellationRequested) return;
            Preview = image; image = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        finally { image?.Dispose(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _stop.Cancel(); _previewCancellation?.Cancel(); _previewCancellation?.Dispose(); _previewCancellation = null;
        var old = Preview; Preview = null; old?.Dispose();
        Selected = null; _projects = []; VisibleProjects = [];
        _stop.Dispose();
    }
}
