using System.Buffers;
using System.Threading.Channels;
using Avalonia.Threading;
using Echo.Desktop.Theming;

namespace Echo.Desktop.Bootstrap;

internal sealed partial class ThemeWallpaper
{
    private sealed record GradeRequest(int Epoch, double Brightness, bool Video, WallpaperStillImage? Still, bool Immediate);
    private readonly Channel<GradeRequest> _grades = Channel.CreateBounded<GradeRequest>(
        new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest },
        dropped => dropped.Still?.Dispose());

    private void NoteBrightness(double brightness)
    {
        if (Math.Abs(_brightness - brightness) <= 0.003) return;
        _brightness = brightness;
        bool hasVideo; lock (_videoGate) hasVideo = _videoW > 0;
        if (hasVideo || _still is not null) ScheduleBrightness(false);
    }

    private void ScheduleBrightness(bool immediate)
    {
        var epoch = Interlocked.Increment(ref _lookEpoch);
        bool hasVideo; lock (_videoGate) hasVideo = _videoW > 0;
        if (!hasVideo && WallpaperGrade.IsNeutral(_brightness))
        { if (_neutral is not null) Present(_neutral); return; }
        var request = new GradeRequest(epoch, _brightness, hasVideo, hasVideo ? null : _still?.Retain(), immediate);
        if (!_grades.Writer.TryWrite(request)) request.Still?.Dispose();
    }

    // One active conversion plus one replaceable request. Pixel snapshots exist only during conversion.
    private async Task GradeLoop(CancellationToken token)
    {
        try
        {
            await foreach (var request in _grades.Reader.ReadAllAsync(token))
            {
                using var still = request.Still;
                try
                {
                    if (!request.Immediate) await Task.Delay(12, token).ConfigureAwait(false);
                    if (request.Epoch != Volatile.Read(ref _lookEpoch)) continue;
                    if (request.Video) { await RegradeVideo(request.Epoch, request.Brightness, token); continue; }
                    if (still is null) continue;
                    using var copy = WallpaperGrade.Snapshot(still.Image);
                    WallpaperGrade.ApplyBrightness(copy, request.Brightness);
                    if (request.Epoch != Volatile.Read(ref _lookEpoch) || token.IsCancellationRequested) continue;
                    var bitmap = WallpaperGrade.ToBitmap(copy);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (request.Epoch != Volatile.Read(ref _lookEpoch) || _disposed) bitmap.Dispose();
                        else Present(bitmap);
                    });
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!_disposed && request.Epoch == Volatile.Read(ref _lookEpoch))
                            _theme.WallpaperStatus = "背景无法读取，请重新选择或切换素材后重试";
                    });
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { while (_grades.Reader.TryRead(out var request)) request.Still?.Dispose(); }
    }
    private async Task RegradeVideo(int epoch, double brightness, CancellationToken token)
    {
        byte[]? raw = null; var width = 0; var height = 0;
        var length = 0;
        lock (_videoGate)
        {
            length = _videoW * _videoH * 4;
            if (_videoFrame is not null && length > 0 && _videoFrame.Length >= length)
            {
                raw = ArrayPool<byte>.Shared.Rent(length);
                _videoFrame.AsSpan(0, length).CopyTo(raw);
                width = _videoW;
                height = _videoH;
            }
        }
        if (raw is null) return;
        try
        {
            if (!WallpaperGrade.IsNeutral(brightness)) WallpaperGrade.ApplyBrightness(raw.AsSpan(0, length), brightness);
            if (epoch != Volatile.Read(ref _lookEpoch) || token.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(() => { if (epoch == Volatile.Read(ref _lookEpoch) && !_disposed) Upload(raw, width, height); });
        }
        finally { if (raw is not null) ArrayPool<byte>.Shared.Return(raw); }
    }
}
