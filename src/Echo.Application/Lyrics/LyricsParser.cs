using System.Globalization;
using System.Text.RegularExpressions;
using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

// Pure, bounded parser. Called by the single reader worker, never during UI layout.
public static partial class LyricsParser
{
    public const int MaxCharacters = 512 * 1024;
    public const int MaxLines = 6000;
    public static LyricsDocument Parse(string text, string format, CancellationToken cancellation = default)
    {
        if (text.Length > MaxCharacters) throw new InvalidDataException("歌词文件过大（最多 512 KiB 文本）。");
        format = format.ToLowerInvariant().TrimStart('.');
        if (format is "ttml" or "xml") return TtmlLyricsParser.Parse(text, cancellation);
        if (format is "srt" or "vtt") return SubtitleLyricsParser.Parse(text, cancellation);
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
                var matches = WordTiming().Matches(content);
                foreach (Match match in matches)
                {
                    var time = Number(match.Groups[1].Value); var duration = Number(match.Groups[2].Value);
                    words.Add(new(match.Groups[3].Value, time, time + duration));
                }
                lines.Add(new(words.Count == 0 ? content : string.Concat(words.Select(w => w.Text)), start, end, words));
                continue;
            }
            var stamps = LineStamp().Matches(row);
            if (stamps.Count > 0)
            {
                var content = LineStamp().Replace(row, "");
                foreach (Match stamp in stamps)
                {
                    var start = Clock(stamp.Groups[1].Value);
                    var words = ParseEnhanced(content);
                    lines.Add(new(WordStamp().Replace(content, ""), start, double.MaxValue, words));
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
            var start = Clock(m.Groups[1].Value);
            var content = text.Substring(m.Index + m.Length, (next?.Index ?? text.Length) - m.Index - m.Length);
            if (content.Length > 0) words.Add(new(content, start, next is null ? start + 1500 : Clock(next.Groups[1].Value)));
        }
        return words;
    }
    internal static LyricsDocument Normalize(List<LyricLine> source, double offset = 0)
    {
        if (source.Count > MaxLines) throw new InvalidDataException("歌词行数超过 6000 行。");
        var ordered = source.OrderBy(l => l.StartMs).ToArray(); var result = new List<LyricLine>();
        foreach (var group in ordered.GroupBy(l => l.StartMs))
        {
            var primary = group.First();
            var secondary = group.Skip(1).Where(l => l.Text != primary.Text).Select(l => l.Text).ToArray();
            result.Add(primary with { Translation = primary.Translation ?? (secondary.Length > 0 ? string.Join(" · ", secondary) : null) });
        }
        for (var i = 0; i < result.Count; i++)
        {
            var line = result[i];
            var end = i + 1 < result.Count ? Math.Min(line.EndMs, result[i + 1].StartMs) : line.EndMs == double.MaxValue ? line.StartMs + 5000 : line.EndMs;
            var words = line.Words.Select(w => w with { EndMs = Math.Max(w.StartMs, Math.Min(w.EndMs, end)) }).ToArray();
            result[i] = line with { EndMs = Math.Max(line.StartMs, end), Words = words };
        }
        return new(result, true, offset);
    }
    internal static double Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : 0;
    internal static double Clock(string value)
    {
        var pieces = value.Replace(',', '.').Split(':'); double seconds = 0;
        foreach (var piece in pieces) seconds = seconds * 60 + Number(piece);
        return Math.Max(0, seconds * 1000);
    }
    [GeneratedRegex(@"\[(\d+:\d{2}(?:[.,]\d+)?)\]", RegexOptions.CultureInvariant)] private static partial Regex LineStamp();
    [GeneratedRegex(@"<(\d+:\d{2}(?:[.,]\d+)?)>", RegexOptions.CultureInvariant)] private static partial Regex WordStamp();
    [GeneratedRegex(@"^\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase)] private static partial Regex OffsetTag();
    [GeneratedRegex(@"^\[(\d+),(\d+)\]")] private static partial Regex LineTiming();
    [GeneratedRegex(@"\((\d+),(\d+)(?:,\d+)?\)([^()]*)")] private static partial Regex WordTiming();
    [GeneratedRegex(@"^\[[a-z]+:.*\]$", RegexOptions.IgnoreCase)] private static partial Regex MetadataTag();
}
