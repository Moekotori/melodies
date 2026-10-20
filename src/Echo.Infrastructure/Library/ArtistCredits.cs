using System.Text;
using System.Text.RegularExpressions;

namespace Echo.Infrastructure.Library;

// File-tag defaults follow Roon's documented delimiter approach, without name guessing.
internal static partial class ArtistCredits
{
    [GeneratedRegex(@"[\u200b-\u200f\u202a-\u202e\u2060\ufeff]")]
    private static partial Regex ZeroWidth();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Display(string name) => Whitespace().Replace(
        ZeroWidth().Replace(name.Normalize(NormalizationForm.FormKC), "").Replace("\0", "; "), " ").Trim();
    public static string Key(string name) => Display(name).ToLowerInvariant();
    public static string[] Split(string credit)
    {
        return DefaultPolicy.Split(credit);
    }
    private static readonly ArtistCreditPolicy DefaultPolicy = new([";"]);
}
