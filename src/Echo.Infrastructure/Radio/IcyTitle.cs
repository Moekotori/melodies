using System.Text;

namespace Echo.Infrastructure.Radio;

public static class IcyTitle
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string? Read(ReadOnlySpan<byte> block)
    {
        var zero = block.IndexOf((byte)0);
        if (zero >= 0) block = block[..zero];
        string text;
        try { text = Utf8.GetString(block); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(block); }
        return Read(text);
    }

    public static string? Read(string block)
    {
        const string key = "StreamTitle='";
        var start = block.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return null;
        start += key.Length;
        var end = start;
        while (end < block.Length && !(block[end] == '\'' && (end + 1 >= block.Length || block[end + 1] == ';'))) end++;
        var title = Collapse(DecodeEntities(block[start..end]));
        return title.Length == 0 ? null : title.Length > 512 ? title[..512] : title;
    }

    private static string DecodeEntities(string value)
    {
        var buffer = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '&') { buffer.Append(value[index]); continue; }
            var semi = value.IndexOf(';', index + 1);
            if (semi < 0 || semi - index > 12) { buffer.Append('&'); continue; }
            var token = value.AsSpan(index, semi - index + 1);
            if (token.Equals("&amp;", StringComparison.OrdinalIgnoreCase)) buffer.Append('&');
            else if (token.Equals("&nbsp;", StringComparison.OrdinalIgnoreCase)) buffer.Append(' ');
            else if (token.Equals("&quot;", StringComparison.OrdinalIgnoreCase)) buffer.Append('"');
            else if (token.Equals("&apos;", StringComparison.OrdinalIgnoreCase)) buffer.Append('\'');
            else if (token.Equals("&lt;", StringComparison.OrdinalIgnoreCase)) buffer.Append('<');
            else if (token.Equals("&gt;", StringComparison.OrdinalIgnoreCase)) buffer.Append('>');
            else if (token.StartsWith("&#x", StringComparison.OrdinalIgnoreCase) && AppendCode(buffer, token[3..^1], 16)) { }
            else if (token.StartsWith("&#", StringComparison.Ordinal) && AppendCode(buffer, token[2..^1], 10)) { }
            else { buffer.Append('&'); continue; }
            index = semi;
        }
        return buffer.ToString();
    }

    private static bool AppendCode(StringBuilder buffer, ReadOnlySpan<char> digits, int radix)
    {
        if (!int.TryParse(digits, radix == 16 ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var code) || code is < 1 or > 0x10FFFF)
            return false;
        buffer.Append(char.ConvertFromUtf32(code));
        return true;
    }

    private static string Collapse(string value)
    {
        var buffer = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character)) { pendingSpace = buffer.Length > 0; continue; }
            if (pendingSpace) buffer.Append(' ');
            pendingSpace = false;
            buffer.Append(character);
        }
        return buffer.ToString();
    }
}
