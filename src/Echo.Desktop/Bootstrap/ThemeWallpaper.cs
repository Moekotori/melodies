using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Echo.Contracts.Media;
using Echo.Contracts.Settings;
using Echo.Desktop.Theming;
using Echo.Infrastructure.Media;
using SkiaSharp;

namespace Echo.Desktop.Bootstrap;

// One latest-request worker, one video frame, one UI acknowledgement. Cancel before replacing media.
internal sealed class ThemeWallpaper : IDisposable
{
    private sealed record Request(string Path, int Revision, CancellationToken Cancellation);
    private readonly ThemeViewModel _theme;
    private readonly Window _window;
    private readonly WallpaperViewport _view;
    private readonly IAppearanceMedia _decoder;
    private readonly Channel<Request> _requests = Channel.CreateBounded<Request>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _active;
    private string _path = "";
    private int _revision;
    private bool _disposed;
    private bool _playing;
    public bool HasImage => _view.Image is not null;
    public event Action? AvailabilityChanged;
    public void InvalidateSource() => _path = "\0";
    private void SetImage(Bitmap? image)
    {
        var old = _view.Image; var available = old is not null;
        _view.Image = image; _theme.WallpaperPreview = image; _view.InvalidateVisual(); old?.Dispose();
        if (available != (image is not null)) AvailabilityChanged?.Invoke();
    }
    public ThemeWallpaper(Window window, ThemeViewModel theme)
    {
        _theme = theme; _window = window; _view = window.FindControl<WallpaperViewport>("ThemeWallpaperLayer")!;
        _decoder = new AppearanceMediaDecoder(AppearanceMediaPath.Resolve()); _ = Task.Run(DecodeLoop);
    }
    public void Apply(ThemePreferences p, ThemeTone tone, bool animationAllowed = true)
    {
        var a = p.Appearance ?? new(); var portrait = _view.Bounds.Height > _view.Bounds.Width && a.PortraitWallpaper.Length > 0;
        _theme.IsPortraitWallpaperActive = portrait;
        var framing = portrait ? a.PortraitFraming ?? new() : new(p.WallpaperFit, p.WallpaperX, p.WallpaperY, a.WallpaperZoom);
        var path = a.WallpaperEnabled ? portrait ? a.PortraitWallpaper : p.Wallpaper : "";
        _view.Opacity = p.WallpaperOpacity / 100; _view.Fit = framing.Fit; _view.PositionX = framing.X / 100; _view.PositionY = framing.Y / 100;
        _view.Zoom = framing.Fit is "Uniform" or "UniformToFill" ? framing.Zoom / 100 : 1; _view.Brightness = a.WallpaperBrightness / 100;
        _view.Effect = p.WallpaperBlur > 0 ? new BlurEffect { Radius = p.WallpaperBlur } : null; _view.InvalidateVisual();
        var visible = _window.IsVisible && _window.WindowState != WindowState.Minimized;
        var running = visible && (!AppearanceMediaDecoder.IsVideo(path) || animationAllowed && (a.VideoPauseMode != "smart" || _window.IsActive));
        if (_path == path && _playing == running) return;
        var changed = _path != path; _path = path; _playing = running; var revision = ++_revision;
        _active?.Cancel(); _active?.Dispose(); _active = null;
        if (changed || path.Length == 0) SetImage(null);
        _theme.WallpaperStatus = path.Length == 0 ? (a.WallpaperEnabled ? "选择本地图片或视频，立即应用到整个窗口" : "背景已停用，素材和效果设置已保留")
            : !running ? "背景已暂停，窗口恢复后继续" : "正在读取背景…";
        if (!running || path.Length == 0) return;
        _active = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _requests.Writer.TryWrite(new(path, revision, _active.Token));
    }
    private async Task DecodeLoop()
    {
        try
        {
            await foreach (var request in _requests.Reader.ReadAllAsync(_stop.Token))
            {
                if (request.Cancellation.IsCancellationRequested) continue;
                try
                {
                    if (AppearanceMediaDecoder.IsVideo(request.Path))
                        await _decoder.ReadVideoAsync(request.Path, (frame, token) => DisplayVideo(request, frame, token), request.Cancellation);
                    else
                    {
                        var image = await ReadImage(request);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (!Current(request)) { image.Dispose(); return; }
                            SetImage(image); _theme.WallpaperStatus = "已应用背景图片";
                        });
                    }
                }
                catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or OperationCanceledException or JsonException or System.ComponentModel.Win32Exception)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => { if (Current(request)) { SetImage(null); _theme.WallpaperStatus = error is InvalidDataException ? error.Message : "背景无法读取，请重新选择或切换素材后重试"; } });
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { _stop.Dispose(); }
    }
    private bool Current(Request r) => !_disposed && r.Revision == _revision && !r.Cancellation.IsCancellationRequested;
    private async Task DisplayVideo(Request request, VideoFrame frame, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!Current(request)) return;
            if (_view.Image is not WriteableBitmap bitmap || bitmap.PixelSize != new PixelSize(frame.Width, frame.Height))
            {
                bitmap = new(new(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                SetImage(bitmap);
            }
            using (var pixels = bitmap.Lock())
                for (var y = 0; y < frame.Height; y++) Marshal.Copy(frame.Bgra, y * frame.Width * 4, pixels.Address + y * pixels.RowBytes, frame.Width * 4);
            _view.InvalidateVisual();
            _theme.WallpaperFrameVersion++;
            _theme.WallpaperStatus = "已应用静音视频背景";
        }, DispatcherPriority.Background);
    }
    private static async Task<Bitmap> ReadImage(Request request)
    {
        await using var file = new FileStream(request.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (file.Length > 16 * 1024 * 1024) throw new InvalidDataException("背景图片最大 16 MiB");
        var bytes = new byte[(int)file.Length]; await file.ReadExactlyAsync(bytes, request.Cancellation);
        using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || codec.Info.Width > 8192 || codec.Info.Height > 8192 || (long)codec.Info.Width * codec.Info.Height > 16 * 1024 * 1024)
            throw new InvalidDataException("图片尺寸过大或无法读取，请选择不超过 1600 万像素的图片");
        using var stream = new MemoryStream(bytes); request.Cancellation.ThrowIfCancellationRequested();
        return codec.Info.Width >= codec.Info.Height ? Bitmap.DecodeToWidth(stream, Math.Min(codec.Info.Width, 1920)) : Bitmap.DecodeToHeight(stream, Math.Min(codec.Info.Height, 1920));
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_revision; _active?.Cancel(); _active?.Dispose(); _active = null;
        _stop.Cancel(); _requests.Writer.TryComplete(); SetImage(null);
    }
}
