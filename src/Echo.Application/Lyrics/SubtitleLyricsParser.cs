using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

internal static partial class SubtitleLyricsParser
{
    public static LyricsDocument Parse(string text, CancellationToken cancellation)
    {
        var lines = new List<LyricLine>();
        foreach (var block in Blocks().Split(text.Replace("\r", "")))
        {
            cancellation.ThrowIfCancellationRequested();
            if (IgnoredBlock().IsMatch(block.TrimStart())) continue;
            var rows = block.Split('\n'); var index = Array.FindIndex(rows, r => r.Contains("-->"));
            if (index < 0) continue;
            var times = rows[index].Split("-->");
            if (times.Length != 2 || !TryTime(times[0], out var start)
                || !TryTime(times[1].Trim().Split(' ', '\t')[0], out var end) || end <= start) continue;
            var body = string.Join("\n", rows.Skip(index + 1));
            var content = new StringBuilder(); var words = new List<LyricWord>(); var cursor = start; var from = 0;
            foreach (Match tag in WordStamp().Matches(body))
            {
                if (!TryTime(tag.Groups[1].Value, out var at) || at < cursor || at > end) continue;
                var fragment = Clean(body[from..tag.Index]); content.Append(fragment);
                if (fragment.Length > 0) words.Add(new(fragment, cursor, at));
                from = tag.Index + tag.Length; cursor = at;
            }
            var tail = Clean(body[from..]); content.Append(tail);
            if (from > 0 && tail.Length > 0) words.Add(new(tail, cursor, end));
            var value = content.ToString().Trim();
            if (value.Length > 4096) throw new InvalidDataException("单行歌词过长。");
            if (value.Length == 0) continue;
            lines.Add(new(value, start, end, words));
            if (lines.Count > LyricsParser.MaxLines) throw new InvalidDataException("歌词行数超过 6000 行。");
        }
        return LyricsParser.Normalize(lines);
    }
    private static string Clean(string text) => WebUtility.HtmlDecode(Tags().Replace(Break().Replace(text, "\n"), ""));
    internal static bool TryTime(string text, out double time)
    {
        time = 0; var match = Timestamp().Match(text.Trim());
        if (!match.Success || LyricsParser.Number(match.Groups[2].Value) > 59 || LyricsParser.Number(match.Groups[3].Value) > 59) return false;
        time = LyricsParser.Number(match.Groups[1].Value) * 3600000 + LyricsParser.Number(match.Groups[2].Value) * 60000
            + LyricsParser.Number(match.Groups[3].Value) * 1000 + LyricsParser.Number(match.Groups[4].Value.PadRight(3, '0'));
        return true;
    }
    [GeneratedRegex(@"\n[\t ]*\n")] private static partial Regex Blocks();
    [GeneratedRegex(@"^(?:NOTE|STYLE|REGION)(?:\s|$)")] private static partial Regex IgnoredBlock();
    [GeneratedRegex(@"^(?:(\d{1,3}):)?(\d{1,2}):(\d{2})[.,](\d{1,3})$")] private static partial Regex Timestamp();
    [GeneratedRegex(@"<((?:\d{1,3}:)?\d{1,2}:\d{2}[.,]\d{1,3})>")] private static partial Regex WordStamp();
    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)] private static partial Regex Break();
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
}
