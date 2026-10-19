using System.Text.Json;
using Echo.Contracts.Lyrics;

namespace Echo.Application.Lyrics;

// Explicit ECHOSteam schema reader; no reflection-based serializer is needed for AOT.
internal static class PortableLyricsParser
{
    public static LyricsDocument Parse(string text, CancellationToken cancellation)
    {
        try
        {
            using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 24 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || String(root, "schema") != "echo.lyrics"
                || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var v) || v != 1
                || !root.TryGetProperty("lines", out var rows) || rows.ValueKind != JsonValueKind.Array
                || rows.GetArrayLength() is 0 or > LyricsParser.MaxLines) throw new InvalidDataException("无效的 ECHO 歌词文档。");
            var plain = String(root, "kind") == "plain";
            var offset = Number(root, "offsetMs", 0);
            if (Math.Abs(offset) > 600000) throw new InvalidDataException("歌词偏移超出范围。");
            var lines = new List<LyricLine>();
            foreach (var row in rows.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested();
                if (row.ValueKind != JsonValueKind.Object) throw new InvalidDataException("无效的歌词行。");
                var start = Number(row, "timeMs", plain ? -1 : double.NaN);
                var end = Number(row, "endMs", double.MaxValue);
                if (!double.IsFinite(start) || start < -1 || end != double.MaxValue && end <= start)
                    throw new InvalidDataException("无效的歌词时间。");
                var words = new List<LyricWord>();
                if (row.TryGetProperty("words", out var segments) && segments.ValueKind != JsonValueKind.Null)
                {
                    if (segments.ValueKind != JsonValueKind.Array || segments.GetArrayLength() > LyricsParser.MaxWordsPerLine)
                        throw new InvalidDataException("无效的逐字歌词。");
                    double previous = -1;
                    foreach (var word in segments.EnumerateArray())
                    {
                        if (word.ValueKind != JsonValueKind.Object) throw new InvalidDataException("无效的逐字片段。");
                        var from = Number(word, "startMs", double.NaN); var to = Number(word, "endMs", double.MaxValue);
                        if (!double.IsFinite(from) || from < Math.Max(0, start) || from < previous || to != double.MaxValue && to <= from)
                            throw new InvalidDataException("无效的逐字时间。");
                        words.Add(new(String(word, "text") ?? "", from, to)); previous = from;
                    }
                }
                lines.Add(new(String(row, "text") ?? "", start, end, words, String(row, "translation"), String(row, "romanization")));
            }
            if (plain || lines.All(line => line.StartMs < 0))
                return new(lines.Select(line => line with { StartMs = 0, EndMs = 0, Words = Array.Empty<LyricWord>() }).ToArray(), false, offset);
            if (lines.Any(line => line.StartMs < 0)) throw new InvalidDataException("同步歌词包含无效时间。");
            return LyricsParser.Normalize(lines, offset);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("无效的 ECHO 歌词文档。", error); }
    }
    private static string? String(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("无效的歌词文本。");
        var text = value.GetString();
        if (text?.Length > 4096) throw new InvalidDataException("单行歌词过长。");
        return text;
    }
    private static double Number(JsonElement element, string property, double fallback)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            throw new InvalidDataException("无效的歌词时间。");
        return number;
    }
}
