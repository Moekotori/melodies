using System.Text.RegularExpressions;
using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

internal static partial class LegacyLyricsParser
{
    public static bool IsTrc(string text) => DurationTag().IsMatch(text);
    public static string CleanTrc(string text) => DurationTag().Replace(text, "");
    public static IReadOnlyList<LyricWord> ParseTrc(string text, double start)
    {
        var words = new List<LyricWord>();
        var matches = DurationTag().Matches(text);
        for (var i = 0; i < matches.Count; i++)
        {
            var tag = matches[i];
            var end = start + LyricsParser.Number(tag.Groups[1].Value);
            var content = text[(tag.Index + tag.Length)..(i + 1 < matches.Count ? matches[i + 1].Index : text.Length)];
            if (content.Length > 0) words.Add(new(content, start, end));
            start = end;
        }
        return words;
    }

    public static LyricsDocument ParseSnc(string text, CancellationToken cancellation)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in text.Split('\n'))
        {
            cancellation.ThrowIfCancellationRequested();
            var row = raw.Trim(); var stamp = SncStamp().Match(row);
            if (stamp.Success)
            {
                if (LyricsParser.Number(stamp.Groups[2].Value) > 59 || LyricsParser.Number(stamp.Groups[3].Value) > 59) continue;
                var start = LyricsParser.Number(stamp.Groups[1].Value) * 3600000 + LyricsParser.Number(stamp.Groups[2].Value) * 60000
                    + LyricsParser.Number(stamp.Groups[3].Value) * 1000 + LyricsParser.Number(stamp.Groups[4].Value) * 10;
                lines.Add(new(row[stamp.Length..].Trim(), start, double.MaxValue, []));
            }
            else if (lines.Count > 0 && row.Length > 0) lines[^1] = lines[^1] with { Text = lines[^1].Text + " " + row };
            if (lines.Count > LyricsParser.MaxLines || lines.Count > 0 && lines[^1].Text.Length > 4096)
                throw new InvalidDataException("SNC 歌词超过行数或单行长度限制。");
        }
        return LyricsParser.Normalize(lines);
    }
    [GeneratedRegex(@"<(\d+)>")] private static partial Regex DurationTag();
    [GeneratedRegex(@"^⑩(\d{2})(\d{2})(\d{2})(\d{2})⑿")] private static partial Regex SncStamp();
}
