namespace Echo.Infrastructure.Radio;

internal static class RadioStreamPlaylist
{
    public const int MaxBytes = 256 * 1024;
    public static string? FirstStream(string text, Uri baseUri)
    {
        if (text.Length > MaxBytes) throw new IOException("在线电台列表超过 256 KiB。");
        if (text.Contains("#EXT-X-", StringComparison.OrdinalIgnoreCase)) return baseUri.AbsoluteUri;
        var pls = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t').StartsWith("[playlist]", StringComparison.OrdinalIgnoreCase);
        using var reader = new StringReader(text);
        var lines = 0;
        while (reader.ReadLine() is { } line)
        {
            if (++lines > 4096) throw new IOException("在线电台列表行数过多。");
            line = line.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('[')) continue;
            var equal = line.IndexOf('=');
            if (pls)
            {
                if (equal < 0) continue;
                var key = line[..equal];
                if (!key.StartsWith("File", StringComparison.OrdinalIgnoreCase) || !int.TryParse(key[4..], out var index) || index < 1) continue;
                line = line[(equal + 1)..].Trim();
            }
            if (!Uri.TryCreate(baseUri, line, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            return RadioHttpAddress.Normalize(uri.AbsoluteUri);
        }
        return null;
    }
}
