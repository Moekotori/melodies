using System.Globalization;
using System.Net;
using System.Text;
using Echo.Contracts.Radio;

namespace Echo.Infrastructure.Radio;

// Two probes at most; direct audio bodies are never consumed or kept by the managed process.
public sealed class HttpRadioStreamResolver : IRadioStreamResolver, IDisposable
{
    private readonly HttpClient client;
    private readonly SemaphoreSlim admission = new(2, 2);
    public HttpRadioStreamResolver(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, MaxConnectionsPerServer = 2, ConnectTimeout = TimeSpan.FromSeconds(4),
            MaxResponseDrainSize = 0, ResponseDrainTimeout = TimeSpan.Zero, PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = RadioStreamPlaylist.MaxBytes };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ECHO/Radio");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    }
    public async Task<RadioStreamInfo> ResolveAsync(string url, CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(6));
        var entered = false;
        try
        {
            await admission.WaitAsync(budget.Token).ConfigureAwait(false); entered = true;
            var next = RadioHttpAddress.Normalize(url);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var depth = 0; depth < 4; depth++)
            {
                if (!seen.Add(next)) throw new IOException("电台列表循环引用，请使用直接音频流地址。");
                using var response = await OpenAsync(next, budget.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new IOException($"电台服务器返回 HTTP {(int)response.StatusCode}，请检查地址或稍后重试。");
                var final = response.RequestMessage!.RequestUri!;
                var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                var codec = Codec(type);
                var playlist = type is "audio/x-mpegurl" or "audio/mpegurl" or "application/x-mpegurl"
                    or "application/vnd.apple.mpegurl" or "audio/x-scpls" or "application/pls+xml"
                    || final.AbsolutePath.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)
                    || final.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                    || final.AbsolutePath.EndsWith(".pls", StringComparison.OrdinalIgnoreCase);
                if (playlist)
                {
                    var text = await ReadPlaylistAsync(response.Content, budget.Token).ConfigureAwait(false);
                    var stream = RadioStreamPlaylist.FirstStream(text, final) ?? throw new IOException("电台列表中没有可播放的 HTTP 音频地址。");
                    if (text.Contains("#EXT-X-", StringComparison.OrdinalIgnoreCase)) return new(final.AbsoluteUri, "HLS");
                    next = stream; continue;
                }
                if (type.Contains("html", StringComparison.Ordinal) || type.Contains("json", StringComparison.Ordinal) || type.StartsWith("text/", StringComparison.Ordinal))
                    throw new IOException("这个地址返回网页或文字，请填写音频流或 M3U / PLS 地址。");
                var bitrate = Header(response, "icy-br", 16);
                return new(final.AbsoluteUri, codec, Header(response, "icy-name", 160), Header(response, "icy-description", 512),
                    Header(response, "icy-genre", 80), int.TryParse(bitrate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number is > 0 and < 100000 ? number : null);
            }
            throw new IOException("电台列表嵌套超过 4 层，请使用直接音频流地址。");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("电台连接超时，请重试或检查网络与地址。"); }
        catch (HttpRequestException) { throw new IOException("无法连接电台服务器，请检查网络与地址后重试。"); }
        finally { if (entered) admission.Release(); }
    }
    private async Task<HttpResponseMessage> OpenAsync(string url, CancellationToken token)
    {
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return response;
            var location = response.Headers.Location; response.Dispose();
            if (location is null || redirect == 5) throw new IOException("电台重定向无效或次数过多。");
            url = RadioHttpAddress.Normalize(new Uri(new Uri(url), location).AbsoluteUri);
        }
        throw new IOException("电台重定向次数过多。");
    }
    private static async Task<string> ReadPlaylistAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength > RadioStreamPlaylist.MaxBytes) throw new IOException("在线电台列表超过 256 KiB。");
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var bytes = new byte[RadioStreamPlaylist.MaxBytes + 1]; var length = 0;
        while (length < bytes.Length)
        { var read = await stream.ReadAsync(bytes.AsMemory(length), token).ConfigureAwait(false); if (read == 0) break; length += read; }
        if (length > RadioStreamPlaylist.MaxBytes) throw new IOException("在线电台列表超过 256 KiB。");
        return Encoding.UTF8.GetString(bytes, 0, length);
    }
    private static string? Header(HttpResponseMessage response, string key, int limit)
    {
        if (!response.Headers.TryGetValues(key, out var values)) return null;
        var value = values.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Any(c => c > 127) && value.All(c => c <= 255))
        { try { value = new UTF8Encoding(false, true).GetString(Encoding.Latin1.GetBytes(value)); } catch (DecoderFallbackException) { } }
        return value[..Math.Min(value.Length, limit)];
    }
    private static string Codec(string type) => type switch
    {
        "audio/mpeg" or "audio/mp3" => "MP3", "audio/aac" or "audio/aacp" or "audio/x-aac" => "AAC",
        "audio/flac" or "audio/x-flac" => "FLAC", "audio/ogg" or "application/ogg" => "OGG",
        "audio/opus" => "Opus", "audio/wav" or "audio/x-wav" or "audio/wave" => "WAV", _ => "—"
    };
    public void Dispose() => client.Dispose(); // Semaphore has no wait handle; active cancellation releases its leases.
}
