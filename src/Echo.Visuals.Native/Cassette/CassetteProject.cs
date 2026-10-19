using System.Text.Json;

namespace Echo.Visuals.Cassette;

internal sealed record CassetteWord(string Text, double Start, double? End);
internal sealed record CassetteLine(string Text, string RasterText, string? Translation, double Start, double? End, CassetteWord[] Words);
internal sealed record CassetteProject(string Title, string Font, double FontScale, double SubScale,
    string Phase, bool Retro, bool Translation, double Seed, double Duration, CassetteLine[] Lines)
{
    public static CassetteProject Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var lines = new List<CassetteLine>(); var characters = 0;
        if (root.TryGetProperty("lines", out var incoming))
        foreach (var line in incoming.EnumerateArray())
        {
            if (lines.Count == 512) throw new InvalidDataException("Cassette lyric line budget exceeded.");
            var text = String(line, "text"); characters += text.Length;
            if (characters > 32768) throw new InvalidDataException("Cassette lyric text budget exceeded.");
            var words = new List<CassetteWord>();
            if (line.TryGetProperty("words", out var values))
            foreach (var word in values.EnumerateArray())
            {
                if (words.Count == 256) throw new InvalidDataException("Cassette word budget exceeded.");
                var start = Number(word, "startMs", double.NaN); var wordText = String(word, "text");
                if (double.IsFinite(start) && wordText.Length > 0) words.Add(new(wordText, start, NullableNumber(word, "endMs")));
            }
            var trimmed = text.Trim(); trimmed = trimmed[..Math.Min(240, trimmed.Length)];
            lines.Add(new(text, trimmed, NullableString(line, "translation"), Number(line, "timeMs", double.NaN), NullableNumber(line, "endMs"), words.ToArray()));
        }
        var font = String(root, "font").Trim();
        return new(String(root, "title"), font.Length > 0 ? font + ", " + CassetteRasterizer.LyricsFont : CassetteRasterizer.LyricsFont,
            Math.Clamp(Nonzero(root, "fontSize", 40) / 40, .5, 1.5), Math.Clamp(Nonzero(root, "secondarySize", 22) / 22, .5, 1.6),
            String(root, "phase", "auto"), !IsFalse(root, "retro"), !IsFalse(root, "translation"), Number(root, "seed", 0),
            Number(root, "durationMs", double.PositiveInfinity), lines.ToArray());
    }
    private static string? NullableString(JsonElement root, string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static string String(JsonElement root, string key, string fallback = "") => NullableString(root, key) ?? fallback;
    private static double? NullableNumber(JsonElement root, string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static double Number(JsonElement root, string key, double fallback) => NullableNumber(root, key) ?? fallback;
    private static double Nonzero(JsonElement root, string key, double fallback) { var n = Number(root, key, fallback); return n == 0 ? fallback : n; }
    private static bool IsFalse(JsonElement root, string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.False;
}
