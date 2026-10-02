using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Contracts.Settings;
using Echo.Desktop.Theming;

using SkiaSharp;

namespace Echo.Desktop.Bootstrap;

// One decoder and one pending request; max 16 MiB encoded / 16M original pixels / 1920 decoded edge.
internal sealed class ThemeWallpaper : IDisposable
{
    private readonly ThemeViewModel _theme;
    private readonly WallpaperViewport _view;
    private readonly Channel<(string Path, int Revision)> _requests = Channel.CreateBounded<(string, int)>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource _stop = new();
    private string _path = "";
    private int _revision;
    private bool _disposed;
    public ThemeWallpaper(Window window, ThemeViewModel theme)
    { _theme = theme; _view = window.FindControl<WallpaperViewport>("ThemeWallpaperLayer")!; _ = Task.Run(DecodeLoop); }
    public void Apply(ThemePreferences preferences, ThemeTone tone)
    {
        _view.Opacity = preferences.WallpaperOpacity / 100;
        _view.Fit = preferences.WallpaperFit; _view.PositionX = preferences.WallpaperX / 100; _view.PositionY = preferences.WallpaperY / 100;
        _view.Effect = new BlurEffect { Radius = Math.Max(preferences.WallpaperBlur, tone.Numbers["panelBlurPx"] * tone.Numbers["glassPercent"] / 80) };
        _view.InvalidateVisual();
        if (_path == preferences.Wallpaper) return;
        _path = preferences.Wallpaper; var revision = ++_revision;
        if (_path.Length == 0) { var old = _view.Image; _view.Image = null; old?.Dispose(); _view.InvalidateVisual(); }
        else _requests.Writer.TryWrite((_path, revision));
    }
    private async Task DecodeLoop()
    {
        try
        {
            await foreach (var request in _requests.Reader.ReadAllAsync(_stop.Token))
            {
                Bitmap? image = null; string? error = null;
                try
                {
                    if (request.Revision != Volatile.Read(ref _revision)) continue;
                    await using var file = new FileStream(request.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                    if (file.Length > 16 * 1024 * 1024) throw new InvalidDataException("背景图片最大 16 MiB");
                    var bytes = new byte[(int)file.Length]; await file.ReadExactlyAsync(bytes, _stop.Token);
                    using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data);
                    if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || codec.Info.Width > 8192 || codec.Info.Height > 8192 || (long)codec.Info.Width * codec.Info.Height > 16 * 1024 * 1024)
                        throw new InvalidDataException("图片尺寸过大或格式无法读取，请选择不超过 1600 万像素的图片");
                    using var stream = new MemoryStream(bytes);
                    image = codec.Info.Width >= codec.Info.Height ? Bitmap.DecodeToWidth(stream, Math.Min(codec.Info.Width, 1920)) : Bitmap.DecodeToHeight(stream, Math.Min(codec.Info.Height, 1920));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                { image?.Dispose(); image = null; error = exception is InvalidDataException ? exception.Message : "背景图片无法读取，请重新选择图片"; }
                var result = image; var message = error;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed || request.Revision != _revision) { result?.Dispose(); return; }
                    var old = _view.Image; _view.Image = result; _view.InvalidateVisual(); old?.Dispose();
                    if (message is not null) _theme.Feedback = message;
                });
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { _stop.Dispose(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_revision; _stop.Cancel(); _requests.Writer.TryComplete();
        var old = _view.Image; _view.Image = null; old?.Dispose();
    }
}

