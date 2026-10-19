using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Echo.Contracts.Media;
using Echo.Desktop.Shell;
using Echo.Platform.Steam;
using Echo.Playback;
using Echo.Wallpapers;
using SkiaSharp;

namespace Echo.Desktop.Bootstrap;

internal sealed partial class ThemeWallpaper
{
    private WallpaperRuntime? _browserRuntime;
    private WallpaperRenderState _browserState = new();
    private double _pointerX = .5, _pointerY = .5;

    private void UpdateBrowserState(bool paused)
    {
        var width = Math.Max(16, _view.Bounds.Width); var height = Math.Max(16, _view.Bounds.Height);
        var scale = Math.Min(1, 960 / Math.Max(width, height));
        var state = new WallpaperRenderState(Math.Max(16, (int)(width * scale)), Math.Max(16, (int)(height * scale)), paused,
            _window.IsActive ? 15 : 5, _pointerX, _pointerY);
        Volatile.Write(ref _browserState, state);
        if (Volatile.Read(ref _browserRuntime) is { } runtime) runtime.State = state;
    }
    private void BrowserPointer(object? sender, PointerEventArgs args)
    {
        var position = args.GetPosition(_view);
        _pointerX = Math.Clamp(position.X / Math.Max(1, _view.Bounds.Width), 0, 1);
        _pointerY = Math.Clamp(position.Y / Math.Max(1, _view.Bounds.Height), 0, 1);
        if (Volatile.Read(ref _browserRuntime) is { } runtime)
            runtime.State = runtime.State with { PointerX = _pointerX, PointerY = _pointerY };
    }
    private async Task ReadBrowser(Request request)
    {
        var runtime = new WallpaperRuntime();
        Volatile.Write(ref _browserRuntime, runtime); runtime.State = Volatile.Read(ref _browserState);
        VideoFrame? pixels = null;
        try
        {
            var browser = WallpaperBrowserLocation.Find();
            var assets = WallpaperBrowserLocation.FindAssets(request.Cancellation);
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "wallpaper-runtime");
            await runtime.RenderAsync(request.Path, browser, assets, cache, async (encoded, token) =>
            {
                using var data = SKData.CreateCopy(encoded); using var codec = SKCodec.Create(data);
                if (codec is null || codec.Info.Width is < 1 or > 1280 || codec.Info.Height is < 1 or > 1280)
                    throw new InvalidDataException("动态壁纸画面无效");
                var width = codec.Info.Width; var height = codec.Info.Height;
                if (pixels is null || pixels.Width != width || pixels.Height != height) pixels = new(width, height, new byte[width * height * 4]);
                var handle = GCHandle.Alloc(pixels.Bgra, GCHandleType.Pinned);
                try
                {
                    if (codec.GetPixels(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque), handle.AddrOfPinnedObject()) != SKCodecResult.Success)
                        throw new InvalidDataException("动态壁纸画面解码失败");
                }
                finally { handle.Free(); }
                await DisplayVideo(request, pixels, token);
            }, _browserMedia, request.Cancellation);
        }
        finally { Volatile.Write(ref _browserRuntime, null); }
    }
    private Func<WallpaperMediaState>? _browserMedia;
    private void AttachBrowserPresentation()
    {
        _window.PointerMoved += BrowserPointer;
        if (_window.DataContext is ShellViewModel { Player: { } player })
            _browserMedia = () =>
            {
                var snapshot = player.Zone.Snapshot.State; var playback = snapshot.Playback; var metadata = playback.Current?.Metadata;
                var level = snapshot.Level;
                var spectrum = level is not null && Stopwatch.GetElapsedTime(level.Timestamp).TotalMilliseconds <= 500 ? level.VisualSpectrum : null;
                return new(metadata?.GetValueOrDefault("title") ?? "", metadata?.GetValueOrDefault("artist") ?? "", metadata?.GetValueOrDefault("album") ?? "",
                    playback.Status == PlaybackStatus.Playing ? 1 : playback.Status == PlaybackStatus.Paused ? 2 : 0,
                    playback.PositionSeconds, playback.DurationSeconds, spectrum);
            };
    }
}
