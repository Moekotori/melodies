using System.Net;
using System.Text.Json;

namespace Echo.Lyrics;

internal sealed class LyricsHttp(HttpClient client)
{
    private const int MaxBytes = 2 * 1024 * 1024;
    public async Task<JsonDocument?> ReadAsync(string url, CancellationToken token, string? referer = null, string? body = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url);
        var qq = referer == "https://y.qq.com/";
        request.Headers.TryAddWithoutValidation("User-Agent", qq
            ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
            : "melodies/1.0 (ECHO Network Lyrics)");
        if (qq) request.Headers.TryAddWithoutValidation("Origin", "https://y.qq.com");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (referer is not null) request.Headers.Referrer = new(referer);
        if (body is not null) request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("网络歌词响应过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var bytes = new byte[16 * 1024];
        int count;
        while ((count = await stream.ReadAsync(bytes, deadline.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > MaxBytes) throw new InvalidDataException("网络歌词响应过大。");
            buffer.Write(bytes, 0, count);
        }
        return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), new() { MaxDepth = 32 });
    }
    public static string Url(string url, params (string Key, string Value)[] values)
        => url + "?" + string.Join('&', values.Select(value => Uri.EscapeDataString(value.Key) + "=" + Uri.EscapeDataString(value.Value)));
    public static JsonElement Field(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) ? item : default;
    public static string Text(JsonElement value, string key)
    {
        var item = Field(value, key);
        return item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ValueKind == JsonValueKind.Number ? item.GetRawText() : "";
    }
    public static double Number(JsonElement value, string key) => Field(value, key) is { ValueKind: JsonValueKind.Number } number && number.TryGetDouble(out var result) && double.IsFinite(result) ? result : 0;
    public static IEnumerable<JsonElement> Items(JsonElement value, int limit = 5)
        => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Take(limit) : [];
    public static string Names(JsonElement value) => string.Join(" / ", Items(value, 16).Select(item => Text(item, "name")).Where(name => name.Length > 0));
}
