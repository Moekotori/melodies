using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace Echo.Desktop.Features.Radio;

// One cover at a time, decoded to the feature-card size. Audio bytes are never read here.
public sealed class RadioCoverLoader : IDisposable
{
    private readonly HttpClient client = new(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 3, ConnectTimeout = TimeSpan.FromSeconds(4) })
    { Timeout = TimeSpan.FromSeconds(6) };
    private int version;
    private bool disposed;
    public Bitmap? Image { get; private set; }
    public event Action? Changed;

    public void Show(string? url)
    {
        var request = Interlocked.Increment(ref version);
        Clear();
        if (disposed || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            return;
        _ = LoadAsync(uri, request);
    }

    private async Task LoadAsync(Uri uri, int request)
    {
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 512 * 1024) return;
            if (response.RequestMessage?.RequestUri is not { } final || final.Scheme is not ("http" or "https") || final.UserInfo.Length > 0) return;
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not null && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && type != "application/octet-stream") return;
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var bytes = new byte[512 * 1024 + 1];
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(length)).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
            }
            if (length == 0 || length > 512 * 1024 || request != Volatile.Read(ref version)) return;
            using var data = SKData.CreateCopy(bytes.AsSpan(0, length));
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width is <= 0 or > 8192 || codec.Info.Height is <= 0 or > 8192
                || (long)codec.Info.Width * codec.Info.Height > 16 * 1024 * 1024) return;
            using var image = new MemoryStream(bytes, 0, length, writable: false);
            var edge = Math.Min(160, Math.Max(codec.Info.Width, codec.Info.Height));
            var bitmap = codec.Info.Width >= codec.Info.Height ? Bitmap.DecodeToWidth(image, Math.Min(edge, codec.Info.Width)) : Bitmap.DecodeToHeight(image, Math.Min(edge, codec.Info.Height));
            Dispatcher.UIThread.Post(() =>
            {
                if (request != version || disposed) { bitmap.Dispose(); return; }
                var previous = Image;
                Image = bitmap;
                Changed?.Invoke();
                previous?.Dispose();
            });
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException) { }
    }

    private void Clear()
    {
        var previous = Image;
        Image = null;
        Changed?.Invoke();
        previous?.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Interlocked.Increment(ref version);
        Clear();
        client.Dispose();
    }
}
