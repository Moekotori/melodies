using System.Net;
using System.Text.Json;

namespace Echo.ArtistMetadata;

internal sealed class ArtistHttp : IDisposable
{
    private readonly HttpClient http;
    private readonly SemaphoreSlim musicBrainz = new(1, 1);
    private long lastRequest;
    public ArtistHttp(HttpMessageHandler? handler)
    {
        http = new(handler ?? new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = false });
        http.Timeout = TimeSpan.FromSeconds(8);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ECHO/1.0 (+https://github.com/moekotori/echosteam)");
    }
    public async Task<JsonDocument> GetAsync(string url, CancellationToken token)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0 ||
            uri.Host is not ("musicbrainz.org" or "zh.wikipedia.org" or "en.wikipedia.org" or "www.wikidata.org")) throw new IOException("Unsupported provider.");
        var limited = uri.Host == "musicbrainz.org";
        if (limited) await musicBrainz.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (limited)
            {
                var delay = 1100 - (Environment.TickCount64 - lastRequest);
                if (delay > 0) await Task.Delay((int)delay, token).ConfigureAwait(false);
                lastRequest = Environment.TickCount64;
            }
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 1024 * 1024) throw new IOException("Provider response exceeds budget.");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream(); var block = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(block, token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > 1024 * 1024) throw new IOException("Provider response exceeds budget.");
                buffer.Write(block, 0, count);
            }
            return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), new() { MaxDepth = 32 });
        }
        finally { if (limited) musicBrainz.Release(); }
    }
    public void Dispose() { http.Dispose(); musicBrainz.Dispose(); }
}
internal static class ArtistJson
{
    public static JsonElement Field(this JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : default;
    public static string? Text(this JsonElement value, string name) => value.Field(name) is { ValueKind: JsonValueKind.String } field ? field.GetString() : null;
    public static IEnumerable<JsonElement> Items(this JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    public static string Key(string text) => string.Concat(text.Normalize(System.Text.NormalizationForm.FormKC).Where(char.IsLetterOrDigit)).ToUpperInvariant();
    public static bool SafeLink(string? text) => Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0;
}
