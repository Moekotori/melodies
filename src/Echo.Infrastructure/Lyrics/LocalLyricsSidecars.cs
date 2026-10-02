namespace Echo.Infrastructure.Lyrics;

internal static class LocalLyricsSidecars
{
    private static readonly HashSet<string> TranslationTags = new(StringComparer.OrdinalIgnoreCase)
    { "t", "zh", "cn", "chn", "chi", "chinese", "tw", "cht", "hant", "hans", "hk", "en", "eng", "ja", "jp", "jpn", "ko", "kr", "kor",
      "fr", "fra", "de", "deu", "es", "spa", "it", "ita", "pt", "por", "ru", "rus", "th", "vi", "vie", "id", "中文", "译文", "翻译", "中译", "英文", "日文", "韩文" };
    private static readonly HashSet<string> RomanizationTags = new(StringComparer.OrdinalIgnoreCase)
    { "r", "rom", "roman", "romanised", "romanization", "romanisation", "romanized", "lr", "py", "pinyin", "罗马音", "罗马字", "拼音" };

    public static (string? Translation, string? Romanization) Companions(string primary, CancellationToken cancellation)
    {
        var directory = Path.GetDirectoryName(primary) ?? "";
        var stem = Path.GetFileNameWithoutExtension(primary);
        var tagged = Parse(primary);
        if (tagged is not null) stem = tagged.Value.Stem;
        string? translation = null; string? romanization = null;
        var preferred = Path.GetExtension(primary);
        foreach (var extension in new[] { preferred }.Concat(LocalLyricsSource.Extensions).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            var t = Path.Combine(directory, stem + ".t" + extension);
            var r = Path.Combine(directory, stem + ".r" + extension);
            if (translation is null && t != primary && File.Exists(t)) translation = t;
            if (romanization is null && r != primary && File.Exists(r)) romanization = r;
        }
        foreach (var path in Entries(directory, cancellation).Order(StringComparer.Ordinal))
        {
            var entry = Parse(path);
            if (path == primary || entry is null || !string.Equals(entry.Value.Stem, stem, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Value.Romanization) romanization ??= path;
            else translation ??= path;
        }
        if (tagged is { Romanization: false }) translation = null;
        if (tagged is { Romanization: true }) romanization = null;
        return (translation, romanization);
    }

    public static IEnumerable<string> Promoted(string audioPath, CancellationToken cancellation)
    {
        var folder = Path.GetDirectoryName(audioPath) ?? "";
        var stems = new[] { Path.GetFileNameWithoutExtension(audioPath), Path.GetFileName(audioPath) };
        foreach (var directory in new[] { folder, Path.Combine(folder, "lyrics") })
        {
            var candidates = Entries(directory, cancellation).Select(path => (Path: path, Tag: Parse(path)))
                .Where(e => e.Tag is not null && stems.Contains(e.Tag.Value.Stem, StringComparer.OrdinalIgnoreCase))
                .OrderBy(e => e.Tag!.Value.Romanization).ThenBy(e => e.Path, StringComparer.Ordinal).ToArray();
            foreach (var candidate in candidates) yield return candidate.Path;
        }
    }

    private static IEnumerable<string> Entries(string directory, CancellationToken cancellation)
    {
        IEnumerator<string> enumerator;
        try { enumerator = Directory.EnumerateFiles(directory).GetEnumerator(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { yield break; }
        using (enumerator)
        {
            // Language aliases are optional; never traverse an unbounded music directory to find one.
            for (var count = 0; count < 2048; count++)
            {
                cancellation.ThrowIfCancellationRequested();
                string path;
                try { if (!enumerator.MoveNext()) yield break; path = enumerator.Current; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { yield break; }
                yield return path;
            }
        }
    }

    private static (string Stem, bool Romanization)? Parse(string path)
    {
        if (!LocalLyricsSource.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return null;
        var stem = Path.GetFileNameWithoutExtension(path);
        var separator = stem.LastIndexOfAny(['.', '_', '-']);
        if (separator <= 0) return null;
        var tag = stem[(separator + 1)..];
        if (RomanizationTags.Contains(tag)) return (stem[..separator], true);
        return TranslationTags.Contains(tag) ? (stem[..separator], false) : null;
    }
}
