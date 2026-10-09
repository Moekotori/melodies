using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace Echo.Desktop.Features.Podcasts;

// One selected-show cover. The original download is released after the 256px decode.
public sealed class PodcastCoverLoader : IDisposable
{
    private readonly HttpClient client = new(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(4) })
    { Timeout = TimeSpan.FromSeconds(8) };
    private int version;
    private bool disposed;
    public Bitmap? Image { get; private set; }
    public event Action? Changed;

    public void Show(string? url)
    {
        var request = Interlocked.Increment(ref version);
        Clear();
        if (disposed || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) return;
        _ = LoadAsync(uri, request);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Interlocked.Increment(ref version);
        Clear();
        client.Dispose();
    }

    private async Task LoadAsync(Uri uri, int request)
    {
        try
        {
            var current = uri;
            for (var hop = 0; hop <= 3; hop++)
            {
                using var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (response.StatusCode is System.Net.HttpStatusCode.MovedPermanently or System.Net.HttpStatusCode.Redirect
                    or System.Net.HttpStatusCode.SeeOther or System.Net.HttpStatusCode.TemporaryRedirect or System.Net.HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location;
                    if (location is null || hop == 3) return;
                    current = new Uri(current, location);
                    if (current.Scheme is not ("http" or "https") || current.UserInfo.Length > 0) return;
                    continue;
                }
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 2 * 1024 * 1024) return;
                var type = response.Content.Headers.ContentType?.MediaType;
                if (type is not null && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && type != "application/octet-stream") return;
                var bytes = await ReadAsync(response).ConfigureAwait(false);
                if (bytes is null || request != Volatile.Read(ref version)) return;
                var bitmap = Decode(bytes);
                if (bitmap is null || request != Volatile.Read(ref version)) { bitmap?.Dispose(); return; }
                Dispatcher.UIThread.Post(() =>
                {
                    if (request != version || disposed) { bitmap.Dispose(); return; }
                    var previous = Image;
                    Image = bitmap;
                    Changed?.Invoke();
                    previous?.Dispose();
                });
                return;
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException) { }
    }

    private static async Task<byte[]?> ReadAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var bytes = new byte[2 * 1024 * 1024 + 1];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(length)).ConfigureAwait(false);
            if (read == 0) break;
            length += read;
        }
        return length is 0 or > 2 * 1024 * 1024 ? null : bytes[..length];
    }

    private static Bitmap? Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width is <= 0 or > 8192 || codec.Info.Height is <= 0 or > 8192
            || (long)codec.Info.Width * codec.Info.Height > 4_000_000) return null;
        using var image = new MemoryStream(bytes, writable: false);
        var edge = Math.Min(256, Math.Max(codec.Info.Width, codec.Info.Height));
        return codec.Info.Width >= codec.Info.Height
            ? Bitmap.DecodeToWidth(image, Math.Min(edge, codec.Info.Width))
            : Bitmap.DecodeToHeight(image, Math.Min(edge, codec.Info.Height));
    }

    private void Clear()
    {
        var previous = Image;
        Image = null;
        Changed?.Invoke();
        previous?.Dispose();
    }
}
