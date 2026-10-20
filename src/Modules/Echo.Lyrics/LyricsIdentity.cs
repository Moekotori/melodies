using System.Text;
using System.Text.RegularExpressions;

namespace Echo.Lyrics;

// Only metadata crosses the provider boundary. Paths and credentials are never part of a query.
public sealed record LyricsIdentity(string Title, string Artist, string Album, double DurationSeconds);

internal sealed record LyricsSong(string Id, LyricsIdentity Identity);

internal static partial class LyricsIdentityPolicy
{
    public static bool CanQuery(LyricsIdentity query) => query.Title.Length is > 0 and <= 512
        && query.Artist.Length is > 0 and <= 512 && query.Album.Length <= 512
        && Normalize(query.Title).Length > 0 && Normalize(query.Artist) is not ("" or "unknownartist" or "未知艺术家")
        && double.IsFinite(query.DurationSeconds) && query.DurationSeconds > 0;

    public static bool Accept(LyricsIdentity query, LyricsIdentity candidate)
    {
        if (!CanQuery(query) || !CanQuery(candidate)) return false;
        if (Versions(query.Title) != Versions(candidate.Title)) return false;
        // Strip only known version/feature suffixes. Arbitrary bracketed titles may be different songs.
        if (Normalize(CleanTitle(query.Title)) != Normalize(CleanTitle(candidate.Title))) return false;
        var left = Credits(query.Artist); var right = Credits(candidate.Artist);
        if (!left.Overlaps(right)) return false;
        return Math.Abs(query.DurationSeconds - candidate.DurationSeconds) < Math.Clamp(query.DurationSeconds * 0.15, 15, 90);
    }

    public static IReadOnlyList<(string Title, string Artist)> Variants(LyricsIdentity query)
        => new[] { (query.Title.Trim(), query.Artist.Trim()), (CleanTitle(query.Title), PrimaryArtist(query.Artist)),
            (CleanTitle(query.Title), "") }.Distinct().Take(3).ToArray();

    public static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var rune in value.Normalize(NormalizationForm.FormKC).EnumerateRunes())
            if (Rune.IsLetterOrDigit(rune)) result.Append(Rune.ToLowerInvariant(rune));
        return result.ToString();
    }

    private static HashSet<string> Credits(string value) => ArtistSeparator().Split(value)
        .Select(Normalize).Where(part => part.Length > 0).ToHashSet(StringComparer.Ordinal);
    private static string PrimaryArtist(string artist) => ArtistSeparator().Split(artist)[0].Trim();
    private static string CleanTitle(string title) => FeatureSuffix().Replace(VersionSuffix().Replace(title.Normalize(NormalizationForm.FormKC), ""), "").Trim();
    private static int Versions(string title)
    {
        var normalized = title.Normalize(NormalizationForm.FormKC);
        return (Live().IsMatch(normalized) ? 1 : 0) | (Remix().IsMatch(normalized) ? 2 : 0)
            | (Instrumental().IsMatch(normalized) ? 4 : 0) | (Short().IsMatch(normalized) ? 8 : 0)
            | (Acoustic().IsMatch(normalized) ? 16 : 0) | (Cover().IsMatch(normalized) ? 32 : 0);
    }
    [GeneratedRegex(@"\s*(?:/|、|,|，|&|＆|;|；|\+|×|\bfeat(?:uring)?\.?\b|\bft\.?\b|\bwith\b)\s*", RegexOptions.IgnoreCase)] private static partial Regex ArtistSeparator();
    [GeneratedRegex(@"\s*[\(\[【](?:[^\)\]】]*(?:\blive\b|\bremaster(?:ed)?\b|\bremix\b|\bacoustic\b|\bver(?:sion)?\b|现场|重制|ライブ)[^\)\]】]*)[\)\]】]\s*$", RegexOptions.IgnoreCase)] private static partial Regex VersionSuffix();
    [GeneratedRegex(@"\s*(?:[\(\[]\s*)?(?:feat\.?|ft\.?|featuring)\s+.+$", RegexOptions.IgnoreCase)] private static partial Regex FeatureSuffix();
    [GeneratedRegex(@"\blive\b|现场|ライブ", RegexOptions.IgnoreCase)] private static partial Regex Live();
    [GeneratedRegex(@"\bremix\b|混音", RegexOptions.IgnoreCase)] private static partial Regex Remix();
    [GeneratedRegex(@"\binstrumental\b|\bkaraoke\b|off[ -]?vocal|伴奏|纯音乐|カラオケ", RegexOptions.IgnoreCase)] private static partial Regex Instrumental();
    [GeneratedRegex(@"\btv[ -]?(?:size|edit|ver)|\bradio[ -]?edit\b|\bshort\b", RegexOptions.IgnoreCase)] private static partial Regex Short();
    [GeneratedRegex(@"\bacoustic\b|不插电", RegexOptions.IgnoreCase)] private static partial Regex Acoustic();
    [GeneratedRegex(@"\bcover\b|翻唱|歌ってみた|カバー", RegexOptions.IgnoreCase)] private static partial Regex Cover();
}
