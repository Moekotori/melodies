using System.Globalization;
using System.Text.RegularExpressions;

namespace Echo.Desktop.Features.Artists;

internal static partial class ArtistMonogram
{
    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}\p{M}]*")]
    private static partial Regex Words();
    public static string Wall(string name)
    {
        var words = Words().Matches(name);
        if (words.Count == 0) return "?";
        var first = Elements(words[0].Value);
        var last = words.Count > 1 ? Elements(words[^1].Value)[0] : first.Count > 1 ? first[^1] : "";
        return (first[0] + last).ToUpperInvariant();
    }
    public static string Hero(string name) => string.Concat(Elements(name.Trim()).Take(2)).ToUpperInvariant();
    public static bool IsCjk(string name) => name.EnumerateRunes().Any(r => r.Value is >= 0x3000 and <= 0x9fff or >= 0xac00 and <= 0xd7ff);
    private static List<string> Elements(string value)
    {
        var values = new List<string>(); var iterator = StringInfo.GetTextElementEnumerator(value);
        while (iterator.MoveNext()) values.Add(iterator.GetTextElement()); return values;
    }
}
