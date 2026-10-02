using System.Threading.Channels;
using Avalonia.Media.Imaging;
using Echo.Contracts.Library;
using SkiaSharp;
using Echo.Contracts.Tasks;

namespace Echo.Desktop.Features.Albums;

// Cache and leases are UI-owned. One fixed worker handles file access and thumbnail decoding.
public sealed class AlbumArtworkCache : IAsyncDisposable
{
    private const long Budget = 32 * 1024 * 1024;
    private readonly IAlbumArtwork _source;
    private readonly IBackgroundWorkAdmission? _admission;
    private readonly Channel<Request> _requests = Channel.CreateBounded<Request>(64);
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.Ordinal);
    private readonly Task _worker;
    private readonly CancellationTokenSource _stop = new();
    private long _clock;
    private bool _closed;
    public long Bytes { get; private set; }
    public int Count => _cache.Count;

    public AlbumArtworkCache(IAlbumArtwork source, IBackgroundWorkAdmission? admission = null)
    { _source = source; _admission = admission; _worker = Task.Run(WorkAsync); }

    public Task<Lease?> AcquireAsync(string path, CancellationToken cancellation) => AcquireAsync(path, false, 320, cancellation);
    public Task<Lease?> AcquireAsync(string path, CancellationToken cancellation, int maxEdge) => AcquireAsync(path, false, Math.Clamp(maxEdge, 64, 1024), cancellation);
    public Task<Lease?> AcquireArtistAsync(string path, CancellationToken cancellation) => AcquireAsync(path, true, 320, cancellation);
    private async Task<Lease?> AcquireAsync(string path, bool artist, int maxEdge, CancellationToken cancellation)
    {
        var key = artist ? "artist:" + path : path;
        if (maxEdge != 320) key += "\0" + maxEdge;
        if (_closed || cancellation.IsCancellationRequested) return null;
        if (_cache.TryGetValue(key, out var cached)) return Rent(key, cached);
        var completion = new TaskCompletionSource<Decoded>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_requests.Writer.TryWrite(new(path, artist, maxEdge, cancellation, completion))) return null;
        var decoded = await completion.Task; var bitmap = decoded.Image;
        if (_closed || cancellation.IsCancellationRequested) { bitmap?.Dispose(); return null; }
        if (_cache.TryGetValue(key, out cached)) { bitmap?.Dispose(); return Rent(key, cached); }
        var bytes = bitmap is null ? 0 : (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
        Trim(bytes);
        if (Bytes + bytes > Budget) { bitmap?.Dispose(); return null; }
        cached = new(bitmap, bytes, decoded.Colors);
        _cache.Add(key, cached);
        Bytes += bytes;
        var lease = Rent(key, cached);
        Trim();
        return lease;
    }

    private Lease Rent(string path, Entry entry)
    { entry.References++; entry.Access = ++_clock; return new(this, path, entry.Image, entry.Colors); }

    private void Release(string path)
    {
        if (_cache.TryGetValue(path, out var entry)) { entry.References--; entry.Access = ++_clock; }
        Trim();
    }

    public void ClearUnused()
    {
        foreach (var pair in _cache.Where(pair => pair.Value.References == 0).ToArray())
        { _cache.Remove(pair.Key); Bytes -= pair.Value.Bytes; pair.Value.Image?.Dispose(); }
    }

    private void Trim(long incoming = 0)
    {
        while (Bytes + incoming > Budget || _cache.Count > 192 || (_closed && _cache.Count > 0))
        {
            KeyValuePair<string, Entry> candidate = default;
            foreach (var pair in _cache)
                if (pair.Value.References == 0 && (candidate.Key is null || pair.Value.Access < candidate.Value.Access))
                    candidate = pair;
            if (candidate.Key is null) break;
            _cache.Remove(candidate.Key);
            Bytes -= candidate.Value.Bytes;
            candidate.Value.Image?.Dispose();
        }
    }

    private async Task WorkAsync()
    {
        await foreach (var request in _requests.Reader.ReadAllAsync())
        {
            Bitmap? bitmap = null;
            ArtworkColorSample? colors = null;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(request.Cancellation, _stop.Token);
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (_admission is not null) await _admission.WaitAsync(BackgroundWorkKind.VisibleArtwork, cancellation.Token).ConfigureAwait(false);
                var bytes = request.Artist ? (_source is IArtistArtwork source ? await source.ReadArtistAsync(request.Path, cancellation.Token).ConfigureAwait(false) : null)
                    : await _source.ReadAsync(request.Path, cancellation.Token).ConfigureAwait(false);
                if (bytes is not null)
                {
                    using var data = SKData.CreateCopy(bytes);
                    using var codec = SKCodec.Create(data);
                    // Bound the decoder's temporary original image as well as its thumbnail output.
                    if (codec is not null && codec.Info.Width > 0 && codec.Info.Height > 0 &&
                        codec.Info.Width <= 8192 && codec.Info.Height <= 8192 &&
                        (long)codec.Info.Width * codec.Info.Height <= 16 * 1024 * 1024)
                    {
                        using var stream = new MemoryStream(bytes, writable: false);
                        bitmap = codec.Info.Width >= codec.Info.Height
                            ? Bitmap.DecodeToWidth(stream, Math.Min(request.MaxEdge, codec.Info.Width))
                            : Bitmap.DecodeToHeight(stream, Math.Min(request.MaxEdge, codec.Info.Height));
                    }
                }
                if (bitmap is not null && request.MaxEdge > 320) colors = ArtworkColorSample.Read(bitmap);
                if (cancellation.IsCancellationRequested) { bitmap?.Dispose(); bitmap = null; }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException or InvalidOperationException)
            { bitmap?.Dispose(); bitmap = null; }
            request.Completion.TrySetResult(new(bitmap, colors));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_closed) return;
        _closed = true; _stop.Cancel(); _requests.Writer.TryComplete();
        await _worker;
        Trim(); _stop.Dispose();
    }

    private sealed record Request(string Path, bool Artist, int MaxEdge, CancellationToken Cancellation, TaskCompletionSource<Decoded> Completion);
    private readonly record struct Decoded(Bitmap? Image, ArtworkColorSample? Colors);
    private sealed class Entry(Bitmap? image, long bytes, ArtworkColorSample? colors)
    { public Bitmap? Image { get; } = image; public ArtworkColorSample? Colors { get; } = colors; public long Bytes { get; } = bytes; public int References; public long Access; }

    public sealed class Lease(AlbumArtworkCache owner, string path, Bitmap? image, ArtworkColorSample? colors = null) : IDisposable
    {
        private AlbumArtworkCache? _owner = owner;
        public Bitmap? Image { get; } = image;
        public ArtworkColorSample? Colors { get; } = colors;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(path);
    }
}
