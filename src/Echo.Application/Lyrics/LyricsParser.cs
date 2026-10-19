using System.Globalization;
using System.Text.RegularExpressions;
using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

// Pure, bounded parser. Called by the single reader worker, never during UI layout.
public static partial class LyricsParser
{
    public const int MaxCharacters = 512 * 1024;
    public const int MaxLines = 6000;
    public const int MaxWordsPerLine = 1024;
    public static LyricsDocument Parse(LyricsText source, CancellationToken cancellation = default)
    {
        var primary = Parse(source.Text, source.Format, cancellation);
        return LyricsCompanionMerger.Merge(primary, ParseCompanion(source.Translation, cancellation),
            ParseCompanion(source.Romanization, cancellation), cancellation);
    }
    private static LyricsDocument? ParseCompanion(LyricsTrackText? track, CancellationToken cancellation)
    {
        if (track is null) return null;
        try { return Parse(track.Text, track.Format, cancellation); }
        catch (Exception error) when (error is InvalidDataException or System.Xml.XmlException) { return null; }
    }
    public static LyricsDocument Parse(string text, string format, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (text.Length > MaxCharacters) throw new InvalidDataException("歌词文件过大（最多 512 KiB 文本）。");
        text = text.TrimStart('\uFEFF');
        format = format.ToLowerInvariant().TrimStart('.');
        if (format is "json" or "echo-lyrics.json") return PortableLyricsParser.Parse(text, cancellation);
        if (format is "ass" or "ssa" || text.Contains("[Events]", StringComparison.OrdinalIgnoreCase))
            return AssLyricsParser.Parse(text, cancellation);
        if (format == "snc" || text.TrimStart().StartsWith('⑩')) return LegacyLyricsParser.ParseSnc(text, cancellation);
        if (format == "lrcx" || LrcxLyricsParser.HasAttachments(text)) return LrcxLyricsParser.Parse(text, cancellation);
        if (format is "qrc" or "krc") return TimedLyricsParser.Parse(text, format, cancellation);
        if (text.TrimStart().StartsWith("<QrcInfos", StringComparison.OrdinalIgnoreCase)) return TimedLyricsParser.Parse(text, "qrc", cancellation);
        if (format is "ttml" or "xml" || text.TrimStart().StartsWith("<tt", StringComparison.Ordinal)) return TtmlLyricsParser.Parse(text, cancellation);
        if (format is "srt" or "vtt" || text.Contains("-->")) return SubtitleLyricsParser.Parse(text, cancellation);
        var lines = new List<LyricLine>(); var plain = new List<LyricLine>(); double offset = 0;
        foreach (var raw in text.Split('\n'))
        {
            cancellation.ThrowIfCancellationRequested();
            if (lines.Count >= MaxLines || plain.Count >= MaxLines) throw new InvalidDataException("歌词行数超过 6000 行。");
            var row = raw.Trim().TrimStart('\uFEFF');
            if (row.Length == 0) continue;
            if (row.Length > 4096) throw new InvalidDataException("单行歌词过长。");
            var shift = OffsetTag().Match(row);
            if (shift.Success) { offset = Number(shift.Groups[1].Value); continue; }
            var yrc = LineTiming().Match(row);
            if (yrc.Success)
            {
                var start = Number(yrc.Groups[1].Value); var end = start + Number(yrc.Groups[2].Value);
                var content = row[yrc.Length..]; var words = new List<LyricWord>();
                if (content.TrimStart().StartsWith('<')) return TimedLyricsParser.Parse(text, "krc", cancellation);
                if (!content.TrimStart().StartsWith('(') && WordTiming().IsMatch(content)) return TimedLyricsParser.Parse(text, "qrc", cancellation);
                var matches = WordTiming().Matches(content);
                foreach (Match match in matches)
                {
                    var time = Number(match.Groups[1].Value); var duration = Number(match.Groups[2].Value);
                    words.Add(new(match.Groups[3].Value, time, time + duration));
                }
                TimedLyricsParser.ResolveRelative(words, start, end - start);
                lines.Add(new(words.Count == 0 ? content : string.Concat(words.Select(w => w.Text)), start, end, words));
                continue;
            }
            var stamps = LineStamp().Matches(row);
            if (stamps.Count > 0)
            {
                var leading = 0;
                foreach (Match stamp in stamps) { if (stamp.Index != leading) break; leading += stamp.Length; }
                var content = row[leading..];
                if (stamps.Count >= 3 && leading == stamps[0].Length && stamps[^1].Index + stamps[^1].Length == row.Length)
                {
                    var bracketWords = ParseEnhanced(row.Replace('[', '<').Replace(']', '>'));
                    lines.Add(new(LineStamp().Replace(row, ""), LrcClock(stamps[0].Groups[1].Value),
                        LrcClock(stamps[^1].Groups[1].Value), bracketWords));
                    continue;
                }
                var firstStart = LrcClock(stamps[0].Groups[1].Value);
                foreach (Match stamp in stamps)
                {
                    if (stamp.Index >= leading) break;
                    if (lines.Count >= MaxLines) throw new InvalidDataException("歌词行数超过 6000 行。");
                    var start = LrcClock(stamp.Groups[1].Value);
                    var words = LegacyLyricsParser.IsTrc(content)
                        ? LegacyLyricsParser.ParseTrc(content, firstStart) : ParseEnhanced(content);
                    if (start != firstStart) words = words.Select(w => w with { StartMs = w.StartMs + start - firstStart, EndMs = w.EndMs + start - firstStart }).ToArray();
                    var end = LegacyLyricsParser.IsTrc(content) && words.Count > 0 ? words[^1].EndMs : double.MaxValue;
                    lines.Add(new(LegacyLyricsParser.CleanTrc(WordStamp().Replace(content, "")), start, end, words));
                }
            }
            else if (!MetadataTag().IsMatch(row)) plain.Add(new(row, 0, 0, []));
        }
        if (lines.Count == 0) return new(plain, false, offset);
        return Normalize(lines, offset);
    }

    private static IReadOnlyList<LyricWord> ParseEnhanced(string text)
    {
        var matches = WordStamp().Matches(text); var words = new List<LyricWord>();
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i]; var next = i + 1 < matches.Count ? matches[i + 1] : null;
            var start = LrcClock(m.Groups[1].Value);
            var content = text.Substring(m.Index + m.Length, (next?.Index ?? text.Length) - m.Index - m.Length);
            if (content.Length > 0) words.Add(new(content, start, next is null ? double.MaxValue : LrcClock(next.Groups[1].Value)));
        }
        return words;
    }
    internal static LyricsDocument Normalize(List<LyricLine> source, double offset = 0)
    {
        if (source.Count > MaxLines) throw new InvalidDataException("歌词行数超过 6000 行。");
        var ordered = source.OrderBy(l => l.StartMs).ToArray(); var result = new List<LyricLine>();
        foreach (var group in ordered.GroupBy(l => l.StartMs))
        {
            var primary = group.OrderByDescending(line => line.Words.Count > 0).First();
            var secondary = group.Where(l => l.Text != primary.Text).Select(l => l.Text).Distinct().ToArray();
            result.Add(primary with { Translation = primary.Translation ?? (secondary.Length > 0 ? string.Join(" · ", secondary) : null) });
        }
        for (var i = 0; i < result.Count; i++)
        {
            var line = result[i];
            var end = i + 1 < result.Count ? Math.Min(line.EndMs, result[i + 1].StartMs) : line.EndMs == double.MaxValue ? line.StartMs + 5000 : line.EndMs;
            if (line.Words.Count > MaxWordsPerLine) throw new InvalidDataException("单行歌词逐字片段超过 1024 个。");
            var words = new List<LyricWord>(line.Words.Count);
            for (var j = 0; j < line.Words.Count; j++)
            {
                var word = line.Words[j];
                var wordText = word.Text;
                if (j == 0 && !line.Text.StartsWith(' ')) wordText = wordText.TrimStart();
                if (j == line.Words.Count - 1 && !line.Text.EndsWith(' ')) wordText = wordText.TrimEnd();
                if (word.Text.Length == 0 || !double.IsFinite(word.StartMs) || word.StartMs < line.StartMs || word.StartMs >= end) continue;
                var wordEnd = word.EndMs;
                if (wordEnd <= word.StartMs || !double.IsFinite(wordEnd) || wordEnd == double.MaxValue)
                    wordEnd = j + 1 < line.Words.Count ? line.Words[j + 1].StartMs : end;
                words.Add(word with { Text = wordText, EndMs = Math.Max(word.StartMs, Math.Min(wordEnd, end)) });
            }
            result[i] = line with { EndMs = Math.Max(line.StartMs, end), Words = words };
        }
        return new(result, true, offset);
    }
    internal static double Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : 0;
    private static double LrcClock(string value)
    {
        var separator = value.LastIndexOf(':');
        if (separator != value.IndexOf(':')) value = value[..separator] + "." + value[(separator + 1)..];
        return Clock(value);
    }
    internal static double Clock(string value)
    {
        var pieces = value.Replace(',', '.').Split(':'); double seconds = 0;
        foreach (var piece in pieces) seconds = seconds * 60 + Number(piece);
        return Math.Max(0, seconds * 1000);
    }
    [GeneratedRegex(@"\[(\d+:\d{2}(?:[.,:]\d+)?)\]", RegexOptions.CultureInvariant)] private static partial Regex LineStamp();
    [GeneratedRegex(@"<(\d+:\d{2}(?:[.,:]\d+)?)>", RegexOptions.CultureInvariant)] private static partial Regex WordStamp();
    [GeneratedRegex(@"^\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase)] private static partial Regex OffsetTag();
    [GeneratedRegex(@"^\[(\d+),(\d+)\]")] private static partial Regex LineTiming();
    [GeneratedRegex(@"\((\d+),(\d+)(?:,\d+)?\)([^()]*)")] private static partial Regex WordTiming();
    [GeneratedRegex(@"^\[[a-z]+:.*\]$", RegexOptions.IgnoreCase)] private static partial Regex MetadataTag();
}
