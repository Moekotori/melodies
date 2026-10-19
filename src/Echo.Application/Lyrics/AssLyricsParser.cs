using System.Text;
using System.Text.RegularExpressions;
using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

// Styles, scripts, attachments and vector commands never execute or enter the renderer.
internal static partial class AssLyricsParser
{
    public static LyricsDocument Parse(string text, CancellationToken cancellation)
    {
        var lines = new List<LyricLine>(); var inEvents = false;
        string[] fields = ["layer", "start", "end", "style", "name", "marginl", "marginr", "marginv", "effect", "text"];
        foreach (var raw in text.Split('\n'))
        {
            cancellation.ThrowIfCancellationRequested(); var row = raw.Trim();
            if (row.StartsWith('[')) { inEvents = row.Equals("[Events]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inEvents) continue;
            if (row.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            { fields = row[7..].Split(',').Select(part => part.Trim().ToLowerInvariant()).ToArray(); continue; }
            if (!row.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase) || fields.Length < 3 || fields[^1] != "text") continue;
            var parts = row[9..].Split(',', fields.Length); if (parts.Length != fields.Length) continue;
            var startIndex = Array.IndexOf(fields, "start"); var endIndex = Array.IndexOf(fields, "end");
            if (startIndex < 0 || endIndex < 0 || !SubtitleLyricsParser.TryTime(parts[startIndex], out var start)
                || !SubtitleLyricsParser.TryTime(parts[endIndex], out var end) || end <= start) continue;
            var content = new StringBuilder(); var words = new List<LyricWord>();
            var drawing = false; double cursor = start; double? duration = null;
            foreach (Match token in Token().Matches(parts[^1]))
            {
                if (token.Groups[1].Success)
                {
                    var commands = token.Groups[1].Value;
                    var draw = Drawing().Match(commands); if (draw.Success) drawing = LyricsParser.Number(draw.Groups[1].Value) != 0;
                    var karaoke = Karaoke().Match(commands);
                    if (karaoke.Success)
                    { cursor += duration ?? 0; duration = LyricsParser.Number(karaoke.Groups[1].Value) * 10; }
                    continue;
                }
                if (drawing) continue;
                var fragment = token.Groups[2].Value.Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ");
                content.Append(fragment);
                if (content.Length > 4096) throw new InvalidDataException("单行歌词过长。");
                if (duration is > 0 && cursor < end && fragment.Length > 0)
                {
                    if (words.Count > 0 && words[^1].StartMs == cursor) words[^1] = words[^1] with { Text = words[^1].Text + fragment };
                    else words.Add(new(fragment, cursor, Math.Min(end, cursor + duration.Value)));
                    if (words.Count > LyricsParser.MaxWordsPerLine) throw new InvalidDataException("逐字片段过多。");
                }
            }
            var value = content.ToString().Trim(); if (value.Length == 0) continue;
            lines.Add(new(value, start, end, words));
            if (lines.Count > LyricsParser.MaxLines) throw new InvalidDataException("歌词行数过多。");
        }
        return LyricsParser.Normalize(lines);
    }
    [GeneratedRegex(@"\{([^}]*)\}|([^{}]+)")] private static partial Regex Token();
    [GeneratedRegex(@"\\p(\d+)")] private static partial Regex Drawing();
    [GeneratedRegex(@"\\[kK](?:f|o)?(\d+)")] private static partial Regex Karaoke();
}
