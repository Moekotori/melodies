using System.Net;
using System.Text.Json;

namespace Echo.AlbumInformation;

internal sealed class MetadataHttp : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _musicBrainz = new(1, 1);
    private long _lastRequest;
    public MetadataHttp(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = false }) : new(handler);
        _http.Timeout = TimeSpan.FromSeconds(8);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ECHO/1.0 (+https://github.com/moekotori/echosteam)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }
    public async Task<JsonDocument> GetAsync(string address, CancellationToken token)
    {
        var uri = new Uri(address);
        if (uri.Scheme != "https" || !(uri.Host == "musicbrainz.org" || uri.Host is "zh.wikipedia.org" or "en.wikipedia.org"))
            throw new IOException("Unsupported metadata provider.");
        var musicBrainz = uri.Host == "musicbrainz.org";
        if (musicBrainz) await _musicBrainz.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (musicBrainz)
            {
                var elapsed = Environment.TickCount64 - _lastRequest;
                if (elapsed < 1100) await Task.Delay((int)(1100 - elapsed), token).ConfigureAwait(false);
                _lastRequest = Environment.TickCount64;
            }
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 2 * 1024 * 1024) throw new IOException("Provider response is too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream(); var chunk = new byte[16 * 1024];
            int count;
            while ((count = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > 2 * 1024 * 1024) throw new IOException("Provider response is too large.");
                buffer.Write(chunk, 0, count);
            }
            return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), new() { MaxDepth = 48 });
        }
        finally { if (musicBrainz) _musicBrainz.Release(); }
    }
    public void Dispose() { _http.Dispose(); _musicBrainz.Dispose(); }
}

internal static class JsonFields
{
    public static string? Text(this JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    public static JsonElement Field(this JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) ? item : default;
    public static IEnumerable<JsonElement> Items(this JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    public static int? Number(this JsonElement value, string name) => value.Field(name).ValueKind == JsonValueKind.Number && value.Field(name).TryGetInt32(out var number) ? number : null;
    public static string[] Unique(this IEnumerable<string?> values, int limit = 16) => values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
    public static string Artist(this JsonElement value) => string.Join("", value.Field("artist-credit").Items().Select(credit => (credit.Text("name") ?? credit.Field("artist").Text("name")) + credit.Text("joinphrase")));
    public static string Normalize(string value) => string.Concat(value.Normalize(System.Text.NormalizationForm.FormKC).Where(char.IsLetterOrDigit)).ToLowerInvariant();
    public static bool SafeLink(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo);
}
