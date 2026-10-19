using System.Buffers;
using System.Threading.Channels;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace Echo.Desktop.Features.Artwork;

// Presentation-only images: one active download/decoder and one replaceable pending URL.
internal sealed class RemoteCoverLoader : IDisposable
{
    private sealed record Request(int Version, Uri Uri);
    private readonly Channel<Request> requests = Channel.CreateBounded<Request>(new BoundedChannelOptions(1)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly HttpClient client;
    private readonly Task running;
    private readonly int maxBytes, maxPixels, edge;
    private readonly TimeSpan timeout;
    private readonly bool automaticRedirects;
    private CancellationTokenSource? active;
    private string? selectedUrl;
    private int version;
    private bool disposed, loading;
    public Bitmap? Image { get; private set; }
    public event Action? Changed;

    public RemoteCoverLoader(int maxBytes, int maxPixels, int edge, int timeoutSeconds, bool automaticRedirects,
        HttpMessageHandler? handler = null)
    {
        this.maxBytes = maxBytes; this.maxPixels = maxPixels; this.edge = edge;
        this.automaticRedirects = automaticRedirects; timeout = TimeSpan.FromSeconds(timeoutSeconds);
        client = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = automaticRedirects,
            MaxAutomaticRedirections = 3, ConnectTimeout = TimeSpan.FromSeconds(4) }) { Timeout = timeout };
        running = Task.Run(WorkAsync);
    }

    public void Show(string? url)
    {
        if (disposed) return;
        var uri = Uri.TryCreate(url, UriKind.Absolute, out var parsed) && Allowed(parsed) ? parsed : null;
        if (uri?.AbsoluteUri == selectedUrl && (loading || Image is not null)) return;
        selectedUrl = uri?.AbsoluteUri;
        var current = Interlocked.Increment(ref version);
        lock (gate) active?.Cancel();
        Replace(null); loading = uri is not null;
        if (uri is not null) requests.Writer.TryWrite(new(current, uri));
    }

    private async Task WorkAsync()
    {
        try
        {
            await foreach (var request in requests.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                lock (gate)
                {
                    if (request.Version != Volatile.Read(ref version) || lifetime.IsCancellationRequested) continue;
                    active = cancellation;
                }
                cancellation.CancelAfter(timeout); // Includes the response body, not just headers.
                Bitmap? bitmap = null;
                try
                {
                    bitmap = await DownloadAsync(request.Uri, cancellation.Token).ConfigureAwait(false);
                    if (cancellation.IsCancellationRequested) { bitmap?.Dispose(); bitmap = null; }
                }
                catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException
                    or InvalidOperationException or ArgumentException or NotSupportedException) { bitmap?.Dispose(); bitmap = null; }
                finally { lock (gate) active = null; }
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (disposed || request.Version != version) { bitmap?.Dispose(); return; }
                    loading = false; Replace(bitmap);
                });
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { client.Dispose(); }
    }

    private async Task<Bitmap?> DownloadAsync(Uri uri, CancellationToken token)
    {
        for (var hop = 0; hop <= 3; hop++)
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!automaticRedirects && response.StatusCode is System.Net.HttpStatusCode.MovedPermanently
                or System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.SeeOther
                or System.Net.HttpStatusCode.TemporaryRedirect or System.Net.HttpStatusCode.PermanentRedirect)
            {
                if (response.Headers.Location is not { } location || hop == 3) return null;
                uri = new Uri(uri, location);
                if (!Allowed(uri)) return null;
                continue;
            }
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes
                || response.RequestMessage?.RequestUri is not { } final || !Allowed(final)) return null;
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not null && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                && type != "application/octet-stream") return null;
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var encoded = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, 16 * 1024), token).ConfigureAwait(false);
                    if (read == 0) break;
                    if (encoded.Length + read > maxBytes) return null;
                    encoded.Write(buffer, 0, read);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            token.ThrowIfCancellationRequested();
            return Decode(encoded);
        }
        return null;
    }

    private Bitmap? Decode(MemoryStream encoded)
    {
        if (encoded.Length == 0) return null;
        using var data = SKData.CreateCopy(encoded.GetBuffer().AsSpan(0, (int)encoded.Length));
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width is <= 0 or > 8192 || codec.Info.Height is <= 0 or > 8192
            || (long)codec.Info.Width * codec.Info.Height > maxPixels) return null;
        encoded.Position = 0;
        return codec.Info.Width >= codec.Info.Height
            ? Bitmap.DecodeToWidth(encoded, Math.Min(edge, codec.Info.Width))
            : Bitmap.DecodeToHeight(encoded, Math.Min(edge, codec.Info.Height));
    }

    private static bool Allowed(Uri uri) => uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0;
    private void Replace(Bitmap? bitmap)
    {
        var previous = Image; Image = bitmap;
        Changed?.Invoke(); previous?.Dispose();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Interlocked.Increment(ref version);
        lock (gate) { active?.Cancel(); lifetime.Cancel(); }
        requests.Writer.TryComplete(); Replace(null);
        _ = running.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
    }
}
