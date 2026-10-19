using System.Threading.Channels;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Controls.Lyrics;

// One encoder and one replaceable pending lease. Cache reference counts remain UI-owned.
internal sealed class SourceSceneArtwork : IDisposable
{
    private sealed record Request(int Version, AlbumArtworkCache.Lease Lease);
    private readonly Channel<Request> requests = Channel.CreateBounded<Request>(new BoundedChannelOptions(1)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest }, value => Release(value.Lease));
    private readonly CancellationTokenSource stop = new();
    private readonly Action<byte[]?> deliver;
    private readonly Task running;
    private int version;
    private bool closed;

    public SourceSceneArtwork(Action<byte[]?> deliver) { this.deliver = deliver; running = Task.Run(Run); }
    public void Show(AlbumArtworkCache.Lease? lease)
    {
        var current = ++version;
        if (lease is not null && !closed && requests.Writer.TryWrite(new(current, lease))) return;
        lease?.Dispose();
    }

    private async Task Run()
    {
        try
        {
            await foreach (var request in requests.Reader.ReadAllAsync(stop.Token))
            {
                byte[]? bytes = null;
                try
                {
                    if (!stop.IsCancellationRequested && request.Version == Volatile.Read(ref version))
                        bytes = Capture(request.Lease.Image);
                }
                catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or NotSupportedException) { }
                finally
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try { if (!closed && request.Version == version) deliver(bytes); }
                        finally { request.Lease.Dispose(); }
                    });
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { while (requests.Reader.TryRead(out var request)) Release(request.Lease); }
    }

    private static byte[]? Capture(Bitmap? image)
    {
        if (image is null || (long)image.PixelSize.Width * image.PixelSize.Height > 1024 * 1024) return null;
        using var stream = new MemoryStream(); image.Save(stream, new PngBitmapEncoderOptions());
        return stream.Length <= 4 * 1024 * 1024 ? stream.ToArray() : null;
    }
    private static void Release(AlbumArtworkCache.Lease lease)
    {
        if (Dispatcher.UIThread.CheckAccess()) lease.Dispose();
        else Dispatcher.UIThread.Post(lease.Dispose);
    }
    public void Dispose()
    {
        if (closed) return;
        closed = true; ++version; stop.Cancel(); requests.Writer.TryComplete();
        _ = running.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
