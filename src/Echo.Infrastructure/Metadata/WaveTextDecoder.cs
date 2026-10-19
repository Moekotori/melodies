using System.Text;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

internal static class WaveTextDecoder
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Little = new UnicodeEncoding(false, false, true);
    private static readonly Encoding Big = new UnicodeEncoding(true, false, true);

    public static string Decode(ReadOnlySpan<byte> bytes, int codePage = 0)
    {
        try
        {
            if (bytes.StartsWith("\uFEFF"u8)) return Clean(Utf8.GetString(bytes[3..]));
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) return Clean(Little.GetString(TrimPairs(bytes[2..])));
            if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) return Clean(Big.GetString(TrimPairs(bytes[2..])));
            if (codePage != 0) return Clean(Legacy.Encoding(codePage).GetString(codePage is 1200 or 1201 ? TrimPairs(bytes) : TrimBytes(bytes)));
            bytes = TrimPairs(bytes);
            if (bytes.Length >= 4 && bytes.Length % 2 == 0)
            {
                var even = 0; var odd = 0;
                for (var i = 0; i < bytes.Length; i++) if (bytes[i] == 0) { if ((i & 1) == 0) even++; else odd++; }
                if (Math.Max(even, odd) >= 2 && Math.Max(even, odd) * 10 >= bytes.Length / 2 * 3)
                    return Clean((odd >= even ? Little : Big).GetString(TrimPairs(bytes)));
            }
            bytes = TrimBytes(bytes);
            try { return Clean(Utf8.GetString(bytes)); }
            catch (DecoderFallbackException)
            {
                string? chinese = null, japanese = null;
                try { chinese = Legacy.Encoding(54936).GetString(bytes); } catch (DecoderFallbackException) { }
                try { japanese = Legacy.Encoding(932).GetString(bytes); } catch (DecoderFallbackException) { }
                // Kana distinguishes a common Japanese legacy case; otherwise retain ECHO's Chinese fallback.
                if (japanese is not null && japanese.Any(c => c is >= '\u3040' and <= '\u30ff')) return Clean(japanese);
                return Clean(chinese ?? Legacy.Encoding(1252).GetString(bytes));
            }
        }
        catch (Exception error) when (error is DecoderFallbackException or ArgumentException)
        { throw new MetadataReadException("wave_tag_encoding"); }
    }

    private static ReadOnlySpan<byte> TrimPairs(ReadOnlySpan<byte> bytes)
    { while (bytes.Length >= 2 && bytes[^1] == 0 && bytes[^2] == 0) bytes = bytes[..^2]; return bytes; }
    private static ReadOnlySpan<byte> TrimBytes(ReadOnlySpan<byte> bytes)
    { while (bytes.Length > 0 && bytes[^1] == 0) bytes = bytes[..^1]; return bytes; }
    private static string Clean(string text) => text.Trim('\0', '\uFEFF').Trim();

    private static class Legacy
    {
        static Legacy() => System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        public static Encoding Encoding(int page) => System.Text.Encoding.GetEncoding(page, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
