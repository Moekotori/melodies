using System.Text;
using System.Text.RegularExpressions;

namespace Echo.Infrastructure.Library;

// Source: old ECHO shared/library/artistIdentity.ts. Ambiguous band punctuation stays literal.
internal static partial class ArtistCredits
{
    [GeneratedRegex(@"\s*(?:/|,(?!\s)|;|；|×)\s*|\s+(?:feat\.?|ft\.?|featuring|with|x)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Separator();
    [GeneratedRegex(@"^[a-z0-9]{1,3}/[a-z0-9]{1,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompactSlash();
    [GeneratedRegex(@"(\p{N})\s*/\s*(?=\p{N})")]
    private static partial Regex NumericSlash();
    [GeneratedRegex(@"[\u200b-\u200f\u202a-\u202e\u2060\ufeff]")]
    private static partial Regex ZeroWidth();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Display(string name) => Whitespace().Replace(ZeroWidth().Replace(name.Normalize(NormalizationForm.FormKC), ""), " ").Trim();
    public static string Key(string name) => Display(name).ToLowerInvariant();
    public static IEnumerable<string> Split(string credit)
    {
        var trimmed = credit.Trim();
        var parts = CompactSlash().IsMatch(trimmed.Normalize(NormalizationForm.FormKC)) ? [trimmed] :
            Separator().Split(NumericSlash().Replace(trimmed, "$1\u0001")).Select(part => part.Replace('\u0001', '/').Trim()).Where(part => part.Length > 0).ToArray();
        // Preserve an unusually dense credit as one literal credit instead of silently dropping people.
        return (parts.Length > 64 ? [trimmed] : parts).Select(Display).Where(part => part.Length > 0).DistinctBy(Key);
    }
}
