using System.Text.RegularExpressions;
using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

internal static partial class SubtitleLyricsParser
{
    public static LyricsDocument Parse(string text, CancellationToken cancellation)
    {
        var lines = new List<LyricLine>();
        foreach (var block in text.Replace("\r", "").Split("\n\n"))
        {
            cancellation.ThrowIfCancellationRequested();
            var rows = block.Split('\n'); var index = Array.FindIndex(rows, r => r.Contains("-->"));
            if (index < 0) continue;
            var times = rows[index].Split("-->");
            var content = Tags().Replace(string.Join("\n", rows.Skip(index + 1)), "").Trim();
            if (content.Length > 4096) throw new InvalidDataException("单行歌词过长。");
            lines.Add(new(content, LyricsParser.Clock(times[0].Trim()), LyricsParser.Clock(times[1].Trim().Split(' ')[0]), []));
            if (lines.Count > LyricsParser.MaxLines) throw new InvalidDataException("歌词行数超过 6000 行。");
        }
        return LyricsParser.Normalize(lines);
    }
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
}
