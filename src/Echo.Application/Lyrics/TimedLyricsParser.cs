using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

internal static partial class TimedLyricsParser
{
    public static LyricsDocument Parse(string text, string format, CancellationToken cancellation)
    {
        if (format == "qrc" && text.TrimStart().StartsWith('<'))
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = LyricsParser.MaxCharacters });
            text = XDocument.Load(reader).Descendants().Attributes()
                .FirstOrDefault(a => a.Name.LocalName == "LyricContent")?.Value
                ?? throw new InvalidDataException("QRC 文件没有歌词内容。");
        }
        var lines = new List<LyricLine>(); double offset = 0;
        string? language = null;
        foreach (var raw in text.Split('\n'))
        {
            cancellation.ThrowIfCancellationRequested();
            var row = raw.Trim().TrimStart('\uFEFF');
            if (row.StartsWith("[language:", StringComparison.OrdinalIgnoreCase) && row.EndsWith(']'))
            { language = row[10..^1]; continue; }
            if (row.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase) && row.EndsWith(']'))
            { offset = LyricsParser.Number(row[8..^1]); continue; }
            var timing = LineTiming().Match(row);
            if (!timing.Success) continue;
            if (row.Length > 4096 || lines.Count >= LyricsParser.MaxLines)
                throw new InvalidDataException("逐字歌词超过行数或单行长度限制。");
            var start = LyricsParser.Number(timing.Groups[1].Value);
            var duration = LyricsParser.Number(timing.Groups[2].Value);
            var content = row[timing.Length..];
            var words = format == "krc" ? ParseKrc(content, start) : ParseQrc(content, start, duration);
            lines.Add(new(words.Count == 0 ? content : string.Concat(words.Select(w => w.Text)), start, start + duration, words));
        }
        if (lines.Count == 0) return LyricsParser.Parse(text, "lrc", cancellation);
        var document = LyricsParser.Normalize(lines, offset);
        return format == "krc" && language is not null ? AddLanguage(document, language, cancellation) : document;
    }

    private static List<LyricWord> ParseKrc(string text, double lineStart)
    {
        var matches = KrcWord().Matches(text); var words = new List<LyricWord>();
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i]; var from = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var start = lineStart + LyricsParser.Number(match.Groups[1].Value);
            if (end > from) words.Add(new(text[from..end], start, start + LyricsParser.Number(match.Groups[2].Value)));
        }
        return words;
    }

    private static List<LyricWord> ParseQrc(string text, double lineStart, double duration)
    {
        var matches = QrcWord().Matches(text); var words = new List<LyricWord>(); var cursor = 0;
        foreach (Match match in matches)
        {
            var start = LyricsParser.Number(match.Groups[1].Value);
            if (match.Index > cursor) words.Add(new(text[cursor..match.Index], start, start + LyricsParser.Number(match.Groups[2].Value)));
            cursor = match.Index + match.Length;
        }
        if (words.Count == 0) return words;
        if (cursor < text.Length) words[^1] = words[^1] with { Text = words[^1].Text + text[cursor..] };
        var relative = words.All(w => w.EndMs <= duration + 50);
        var absolute = words.All(w => w.StartMs >= Math.Max(0, lineStart - 50) && w.EndMs <= lineStart + duration + 50);
        if (relative && !absolute)
            for (var i = 0; i < words.Count; i++) words[i] = words[i] with { StartMs = words[i].StartMs + lineStart, EndMs = words[i].EndMs + lineStart };
        return words;
    }

    private static LyricsDocument AddLanguage(LyricsDocument document, string encoded, CancellationToken cancellation)
    {
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            if (!json.RootElement.TryGetProperty("content", out var tracks) || tracks.ValueKind != JsonValueKind.Array) return document;
            var lines = document.Lines.ToArray();
            foreach (var track in tracks.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested();
                if (track.ValueKind != JsonValueKind.Object || !track.TryGetProperty("type", out var type)
                    || !type.TryGetInt32(out var kind) || kind is not (0 or 1)
                    || !track.TryGetProperty("lyricContent", out var rows) || rows.ValueKind != JsonValueKind.Array) continue;
                var index = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (index >= lines.Length) break;
                    var value = ReadLanguageRow(row);
                    if (!string.IsNullOrWhiteSpace(value)) lines[index] = kind == 1
                        ? lines[index] with { Translation = lines[index].Translation ?? value }
                        : lines[index] with { Romanization = lines[index].Romanization ?? value };
                    index++;
                }
            }
            return document with { Lines = lines };
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException)
        { return document; } // Broken optional language metadata does not discard the main lyrics.
    }

    private static string? ReadLanguageRow(JsonElement row)
    {
        var text = new StringBuilder();
        if (row.ValueKind == JsonValueKind.String) text.Append(row.GetString());
        else if (row.ValueKind == JsonValueKind.Array)
            foreach (var part in row.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String) text.Append(part.GetString());
                if (text.Length > 4096) return null;
            }
        return text.Length <= 4096 ? text.ToString().Trim() : null;
    }

    [GeneratedRegex(@"^\[(\d+),(\d+)\]")] private static partial Regex LineTiming();
    [GeneratedRegex(@"<(\d+),(\d+)(?:,\d+)?>")] private static partial Regex KrcWord();
    [GeneratedRegex(@"\((\d+),(\d+)(?:,\d+)?\)")] private static partial Regex QrcWord();
}
