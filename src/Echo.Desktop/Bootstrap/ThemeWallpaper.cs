using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Echo.Contracts.Media;
using Echo.Contracts.Settings;
using Echo.Desktop.Theming;
using Echo.Infrastructure.Media;
using SkiaSharp;
using Echo.Wallpapers;

namespace Echo.Desktop.Bootstrap;

// One latest-request worker, one video frame, one UI acknowledgement. Cancel before replacing media.
internal sealed partial class ThemeWallpaper : IDisposable
{
    private sealed record Request(string Path, int Revision, CancellationToken Cancellation);
    private readonly ThemeViewModel _theme;
    private readonly Window _window;
    private readonly WallpaperViewport _view;
    private readonly IAppearanceMedia _decoder;
    private readonly Channel<Request> _requests = Channel.CreateBounded<Request>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource _stop = new();
    private readonly object _videoGate = new();
    private CancellationTokenSource? _active;
    private WallpaperStillImage? _still;
    private Bitmap? _neutral => _still?.Image;
    private readonly Task _decodeTask;
    private readonly Task _gradeTask;
    private Bitmap? _shown;
    private byte[]? _videoFrame;
    private int _videoW;
    private int _videoH;
    private int _lookEpoch;
    private double _brightness = 1;
    private string _path = "";
    private int _revision;
    private bool _disposed;
    private bool _playing;
    public bool HasImage => _view.Image is not null;
    public event Action? AvailabilityChanged;
    public void InvalidateSource() => _path = "\0";
    public ThemeWallpaper(Window window, ThemeViewModel theme)
    {
        _theme = theme; _window = window; _view = window.FindControl<WallpaperViewport>("ThemeWallpaperLayer")!;
        AttachBrowserPresentation();
        _decoder = new AppearanceMediaDecoder(AppearanceMediaPath.Resolve(), AppearanceMediaPath.AnalysisHost());
        var token = _stop.Token;
        _decodeTask = Task.Run(() => DecodeLoop(token));
        _gradeTask = Task.Run(() => GradeLoop(token));
    }
    public void Apply(ThemePreferences p, ThemeTone tone, bool animationAllowed = true)
    {
        var a = p.Appearance ?? new(); var portrait = _view.Bounds.Height > _view.Bounds.Width && a.PortraitWallpaper.Length > 0;
        _theme.IsPortraitWallpaperActive = portrait;
        var framing = portrait ? a.PortraitFraming ?? new() : new(p.WallpaperFit, p.WallpaperX, p.WallpaperY, a.WallpaperZoom);
        var path = a.WallpaperEnabled ? portrait ? a.PortraitWallpaper : p.Wallpaper : "";
        _view.Opacity = p.WallpaperOpacity / 100; _view.Fit = framing.Fit; _view.PositionX = framing.X / 100; _view.PositionY = framing.Y / 100;
        _view.Zoom = framing.Fit is "Uniform" or "UniformToFill" ? framing.Zoom / 100 : 1; _view.Blur = p.WallpaperBlur;
        NoteBrightness(a.WallpaperBrightness / 100); _view.InvalidateVisual();
        var visible = _window.IsVisible && _window.WindowState != WindowState.Minimized;
        var project = WallpaperPaths.IsProject(path);
        UpdateBrowserState(a.VideoPauseMode == "smart" && !_window.IsActive);
        // A visible project freezes in place under the explicit pause setting; hidden projects release their process.
        var running = visible && (project || !AppearanceMediaDecoder.IsVideo(path) || animationAllowed && (a.VideoPauseMode != "smart" || _window.IsActive));
        if (_path == path && _playing == running) return;
        var changed = _path != path; _path = path; _playing = running; var revision = ++_revision;
        _active?.Cancel(); _active?.Dispose(); _active = null;
        if (changed || path.Length == 0) ClearMedia();
        _theme.WallpaperStatus = path.Length == 0 ? (a.WallpaperEnabled ? "选择本地图片或视频，立即应用到整个窗口" : "背景已停用，素材和效果设置已保留")
            : !running ? "背景已暂停，窗口恢复后继续" : "正在读取背景…";
        if (!running || path.Length == 0) return;
        if (!changed && _still is not null) { _theme.WallpaperStatus = "已应用背景图片"; return; }
        _active = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _requests.Writer.TryWrite(new(path, revision, _active.Token));
    }
    private void Remember(Bitmap image)
    {
        var retired = _still;
        _still = new WallpaperStillImage(image);
        Present(image, retired?.Image);
        retired?.Dispose();
        if (!WallpaperGrade.IsNeutral(_brightness)) ScheduleBrightness(true);
    }
    private void Present(Bitmap? image, Bitmap? retiringSource = null)
    {
        var previous = _shown; var available = previous is not null; _shown = image;
        _view.Image = image; _theme.WallpaperPreview = image; _view.InvalidateVisual();
        if (previous is not null && previous != image && previous != _neutral && previous != retiringSource) previous.Dispose();
        if (available != (image is not null)) AvailabilityChanged?.Invoke();
    }
    private void ClearMedia()
    {
        Interlocked.Increment(ref _lookEpoch);
        var still = _still; var neutral = _neutral; _still = null;
        lock (_videoGate) { _videoFrame = null; _videoW = 0; _videoH = 0; }
        var shown = _shown; _shown = null;
        var available = _view.Image is not null;
        _view.Image = null; _theme.WallpaperPreview = null; _view.InvalidateVisual();
        if (shown is not null && shown != neutral) shown.Dispose();
        still?.Dispose();
        if (available) AvailabilityChanged?.Invoke();
    }
    private async Task DecodeLoop(CancellationToken token)
    {
        try
        {
            await foreach (var request in _requests.Reader.ReadAllAsync(token))
            {
                if (request.Cancellation.IsCancellationRequested) continue;
                try
                {
                    if (WallpaperPaths.IsProject(request.Path))
                        await ReadBrowser(request);
                    else if (AppearanceMediaDecoder.IsVideo(request.Path))
                        await _decoder.ReadVideoAsync(request.Path, (frame, token) => DisplayVideo(request, frame, token), request.Cancellation);
                    else
                    {
                        var image = await ReadImage(request);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (!Current(request)) { image.Dispose(); return; }
                            Remember(image); _theme.WallpaperStatus = "已应用背景图片";
                        });
                    }
                }
                catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or OperationCanceledException or JsonException or System.Net.Http.HttpRequestException or System.ComponentModel.Win32Exception)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => { if (Current(request)) { ClearMedia(); _theme.WallpaperStatus = error is InvalidDataException ? error.Message : "背景无法读取，请重新选择或切换素材后重试"; } });
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private bool Current(Request r) => !_disposed && r.Revision == _revision && !r.Cancellation.IsCancellationRequested;
    private async Task DisplayVideo(Request request, VideoFrame frame, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_videoGate)
        {
            if (_videoFrame is null || _videoFrame.Length != frame.Bgra.Length) _videoFrame = new byte[frame.Bgra.Length];
            frame.Bgra.AsSpan().CopyTo(_videoFrame);
            _videoW = frame.Width;
            _videoH = frame.Height;
        }
        var epoch = Interlocked.Increment(ref _lookEpoch);
        var brightness = _brightness;
        var length = frame.Bgra.Length;
        var graded = WallpaperGrade.IsNeutral(brightness) ? null : ArrayPool<byte>.Shared.Rent(length);
        try
        {
            byte[] pixels;
            if (graded is null)
                pixels = frame.Bgra;
            else
            {
                frame.Bgra.AsSpan().CopyTo(graded);
                WallpaperGrade.ApplyBrightness(graded.AsSpan(0, length), brightness);
                pixels = graded;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!Current(request) || epoch != Volatile.Read(ref _lookEpoch)) return;
                Upload(pixels, frame.Width, frame.Height);
                _theme.WallpaperStatus = WallpaperPaths.IsProject(request.Path) ? "已应用 Wallpaper Engine 动态背景" : "已应用静音视频背景";
            }, DispatcherPriority.Background);
        }
        finally
        {
            if (graded is not null) ArrayPool<byte>.Shared.Return(graded);
        }
    }
    private void Upload(byte[] pixels, int width, int height)
    {
        var bitmap = _view.Image as WriteableBitmap;
        var created = bitmap is null || bitmap.PixelSize != new PixelSize(width, height);
        if (created) bitmap = new(new(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var locked = bitmap!.Lock())
            for (var y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, locked.Address + y * locked.RowBytes, width * 4);
        if (created) Present(bitmap);
        _theme.WallpaperFrameVersion++; _view.ContentRevision = _theme.WallpaperFrameVersion;
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
        _window.PointerMoved -= BrowserPointer; _browserMedia = null;
        _stop.Cancel(); _requests.Writer.TryComplete(); _grades.Writer.TryComplete(); ClearMedia();
        _ = Task.WhenAll(_decodeTask, _gradeTask).ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
