using System.Text;
using System.Text.RegularExpressions;
using Echo.Contracts.Podcast;

namespace Echo.Infrastructure.Podcast;

internal static partial class PodcastFeedDecoder
{
    public static string Decode(ReadOnlySpan<byte> bytes, string? contentType)
    {
        Encoding encoding;
        ReadOnlySpan<byte> body = bytes;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        { encoding = Utf8(); body = bytes[3..]; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        { encoding = new UnicodeEncoding(false, false, true); body = bytes[2..]; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        { encoding = new UnicodeEncoding(true, false, true); body = bytes[2..]; }
        else if (bytes.Length >= 4 && bytes[0] == 0x3C && bytes[1] == 0 && bytes[2] == 0x3F && bytes[3] == 0)
            encoding = new UnicodeEncoding(false, false, true);
        else if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0x3C && bytes[2] == 0 && bytes[3] == 0x3F)
            encoding = new UnicodeEncoding(true, false, true);
        else encoding = Named(Charset(contentType) ?? XmlEncoding(bytes) ?? "utf-8");
        try { return encoding.GetString(body); }
        catch (DecoderFallbackException) { throw new PodcastFailure("invalid_feed"); }
    }

    private static Encoding Utf8() => new UTF8Encoding(false, true);

    private static Encoding Named(string name) => name.ToLowerInvariant() switch
    {
        "utf-8" or "utf8" => Utf8(),
        "utf-16" or "utf-16le" => new UnicodeEncoding(false, false, true),
        "utf-16be" => new UnicodeEncoding(true, false, true),
        "iso-8859-1" or "latin1" or "us-ascii" or "ascii" => Encoding.Latin1,
        _ => throw new PodcastFailure("invalid_feed")
    };

    private static string? Charset(string? contentType)
    {
        if (contentType is null) return null;
        var match = CharsetPattern().Match(contentType);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? XmlEncoding(ReadOnlySpan<byte> bytes)
    {
        var length = Math.Min(bytes.Length, 512);
        var prefix = Encoding.Latin1.GetString(bytes[..length]);
        var match = XmlPattern().Match(prefix);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"charset\s*=\s*[""']?([\w.-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CharsetPattern();

    [GeneratedRegex(@"^\s*<\?xml\b[^?]*\bencoding\s*=\s*[""']([\w.-]+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex XmlPattern();
}
