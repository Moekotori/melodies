using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

internal static partial class LrcxLyricsParser
{
    public static bool HasAttachments(string text) => Attachment().IsMatch(text);
    public static LyricsDocument Parse(string text, CancellationToken cancellation)
    {
        var rows = text.Split('\n');
        var document = LyricsParser.Parse(string.Join('\n', rows.Where(row => !Attachment().IsMatch(row))), "lrc", cancellation);
        var lines = document.Lines.ToArray();
        var byTime = lines.Select((line, index) => (line.StartMs, index)).ToDictionary(pair => pair.StartMs, pair => pair.index);
        foreach (var row in rows)
        {
            cancellation.ThrowIfCancellationRequested();
            var match = Attachment().Match(row); if (!match.Success) continue;
            var stamp = LyricsParser.Parse(match.Groups[1].Value + "_", "lrc", cancellation);
            if (stamp.Lines.Count == 0 || !byTime.TryGetValue(stamp.Lines[0].StartMs, out var index)) continue;
            var line = lines[index]; var body = match.Groups[3].Value.Trim();
            if (body.Length > 4096) throw new InvalidDataException("单行歌词过长。");
            var tag = match.Groups[2].Value.ToLowerInvariant();
            if (tag == "tr") lines[index] = line with { Translation = line.Translation ?? body };
            else if (tag == "tt")
            {
                var chars = StringInfo.ParseCombiningCharacters(line.Text);
                var marks = Timing().Matches(body); var words = new List<LyricWord>();
                if (marks.Count > LyricsParser.MaxWordsPerLine + 1) throw new InvalidDataException("LRCX 逐字片段过多。");
                var valid = true; double lastTime = -1; var lastIndex = -1;
                foreach (Match mark in marks)
                {
                    var at = LyricsParser.Number(mark.Groups[1].Value); var ch = LyricsParser.Number(mark.Groups[2].Value);
                    if (ch > chars.Length || ch <= lastIndex || at < lastTime) { valid = false; break; }
                    lastIndex = (int)ch; lastTime = at;
                }
                if (!valid) continue;
                var duration = Duration().Match(body);
                for (var i = 0; i < marks.Count; i++)
                {
                    var from = (int)LyricsParser.Number(marks[i].Groups[2].Value);
                    var to = i + 1 < marks.Count ? (int)LyricsParser.Number(marks[i + 1].Groups[2].Value) : chars.Length;
                    var start = line.StartMs + LyricsParser.Number(marks[i].Groups[1].Value);
                    var end = i + 1 < marks.Count ? line.StartMs + LyricsParser.Number(marks[i + 1].Groups[1].Value)
                        : duration.Success ? line.StartMs + LyricsParser.Number(duration.Groups[1].Value) : line.EndMs;
                    if (to > from) words.Add(new(line.Text[chars[from]..(to < chars.Length ? chars[to] : line.Text.Length)], start, end));
                }
                lines[index] = line with { Words = words };
            }
            else if (tag is "ro" or "fu")
            {
                var chars = StringInfo.ParseCombiningCharacters(line.Text); var cursor = 0; var reading = new StringBuilder();
                foreach (Match part in Reading().Matches(body))
                {
                    var from = (int)Math.Min(int.MaxValue, LyricsParser.Number(part.Groups[2].Value));
                    var to = (int)Math.Min(int.MaxValue, LyricsParser.Number(part.Groups[3].Value));
                    if (from < cursor || to <= from || to > chars.Length) continue;
                    var begin = cursor < chars.Length ? chars[cursor] : line.Text.Length;
                    reading.Append(line.Text.AsSpan(begin, chars[from] - begin)).Append(part.Groups[1].Value); cursor = to;
                }
                if (reading.Length > 0)
                {
                    reading.Append(line.Text.AsSpan(cursor < chars.Length ? chars[cursor] : line.Text.Length));
                    lines[index] = line with { Romanization = reading.ToString() };
                }
            }
        }
        return LyricsParser.Normalize(lines.ToList(), document.OffsetMs);
    }
    [GeneratedRegex(@"(?m)^\s*(\[\d+:\d{2}(?:[.:,]\d+)?\])\[(tr|tt|ro|fu)(?::[^\]]+)?\](.*)$", RegexOptions.IgnoreCase)] private static partial Regex Attachment();
    [GeneratedRegex(@"<(\d+),(\d+)>")] private static partial Regex Timing();
    [GeneratedRegex(@"<(\d+)>\s*$")] private static partial Regex Duration();
    [GeneratedRegex(@"<([^,>]+),(\d+),(\d+)>")] private static partial Regex Reading();
}
