using System.Net;
using System.Text;

namespace Echo.Bridge;

internal sealed class LanRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required string Query { get; init; }
    public required Dictionary<string, string> Headers { get; init; }
    public required byte[] Body { get; init; }
    public string Header(string name) => Headers.TryGetValue(name, out var value) ? value : "";

    public Dictionary<string, string> Form()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(result, Query);
        if (Header("content-type").Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            Add(result, Encoding.UTF8.GetString(Body));
        return result;
    }

    private static void Add(Dictionary<string, string> result, string text)
    {
        foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries).Take(16))
        {
            var cut = pair.IndexOf('=');
            if (cut <= 0) continue;
            var key = Unescape(pair[..cut]);
            var value = Unescape(pair[(cut + 1)..]);
            if (key.Length is > 0 and <= 40 && value.Length <= 2048) result[key] = value;
        }
    }

    private static string Unescape(string value)
    {
        try { return Uri.UnescapeDataString(value.Replace('+', ' ')); }
        catch (UriFormatException) { return ""; }
    }
}

internal static class LanHttp
{
    public static async Task<LanRequest?> ReadAsync(Stream stream, int maxBody, CancellationToken token)
    {
        var header = new MemoryStream();
        var buffer = new byte[1024];
        var split = -1;
        while (header.Length < 16384 && split < 0)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) return header.Length == 0 ? null : throw new IOException("请求头不完整。");
            header.Write(buffer, 0, read);
            split = FindHeaderEnd(header.GetBuffer(), (int)header.Length);
        }
        if (split < 0) throw new IOException("请求头过大。");
        var text = Encoding.ASCII.GetString(header.GetBuffer(), 0, split);
        var lines = text.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2) throw new IOException("无法识别的请求。");
        var target = parts[1];
        var query = target.IndexOf('?');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon > 0) headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }
        var body = new MemoryStream();
        var already = (int)header.Length - (split + 4);
        if (already > 0) body.Write(header.GetBuffer(), split + 4, already);
        if (headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var length) && length > 0)
        {
            if (length > maxBody) throw new IOException("请求体超过限制。");
            while (body.Length < length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, length - (int)body.Length)), token).ConfigureAwait(false);
                if (read == 0) throw new IOException("请求体不完整。");
                body.Write(buffer, 0, read);
            }
        }
        return new LanRequest
        {
            Method = parts[0].ToUpperInvariant(),
            Path = query < 0 ? target : target[..query],
            Query = query < 0 ? "" : target[(query + 1)..],
            Headers = headers,
            Body = body.ToArray()
        };
    }

    public static Task WriteAsync(Stream stream, int status, string type, byte[] body, CancellationToken token, params (string Name, string Value)[] headers)
    {
        var reason = status switch { 200 => "OK", 206 => "Partial Content", 303 => "See Other", 404 => "Not Found", 416 => "Range Not Satisfiable", 500 => "Error", _ => "OK" };
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        builder.Append("Content-Type: ").Append(type).Append("\r\n");
        builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        builder.Append("Connection: close\r\n");
        foreach (var header in headers) builder.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        builder.Append("\r\n");
        var prefix = Encoding.ASCII.GetBytes(builder.ToString());
        return WriteAllAsync(stream, prefix, body, token);
    }

    public static async Task CopyAsync(Stream source, Stream destination, long count, CancellationToken token)
    {
        var buffer = new byte[65536];
        while (count > 0)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), token).ConfigureAwait(false);
            if (read == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            count -= read;
        }
    }

    private static async Task WriteAllAsync(Stream stream, byte[] prefix, byte[] body, CancellationToken token)
    {
        await stream.WriteAsync(prefix, token).ConfigureAwait(false);
        if (body.Length > 0) await stream.WriteAsync(body, token).ConfigureAwait(false);
    }

    private static int FindHeaderEnd(byte[] data, int length)
    {
        for (var i = 0; i + 3 < length; i++)
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n') return i;
        return -1;
    }
}
