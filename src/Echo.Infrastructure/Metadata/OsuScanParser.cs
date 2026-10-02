using System.Globalization;

namespace Echo.Infrastructure.Metadata;

// Port of parseOsuFileMetadata plus OsuScanText projection. Span scanning skips allocation for HitObjects.
internal static class OsuScanParser
{
    public static OsuScanFacts Parse(string text, CancellationToken cancellation)
    {
        var section = ""; string? audio = null, title = null, artist = null, creator = null, version = null;
        string? beatmap = null, set = null, cover = null; double? bpm = null; var storyboard = false;
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            cancellation.ThrowIfCancellationRequested();
            var newline = remaining.IndexOf('\n');
            var line = (newline < 0 ? remaining : remaining[..newline]).Trim();
            remaining = newline < 0 ? [] : remaining[(newline + 1)..];
            if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
            { section = line[1..^1].ToString().ToLowerInvariant(); continue; }
            if (line.IsEmpty || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (section == "events")
            {
                if (line.StartsWith("Sprite,", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Animation,", StringComparison.OrdinalIgnoreCase)) storyboard = true;
                if (cover is null && (line.StartsWith("0,", StringComparison.Ordinal) ||
                    line.StartsWith("Background,", StringComparison.OrdinalIgnoreCase)))
                {
                    var fields = Csv(line);
                    if (fields.Count > 2) cover = fields[2];
                }
                continue;
            }
            if (section == "timingpoints" && bpm is null)
            {
                var fields = Csv(line);
                if (fields.Count > 1 && double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var length) &&
                    length > 0 && (fields.Count <= 6 || fields[6] is "" or "1"))
                {
                    var timingBpm = 60000 / length;
                    if (double.IsFinite(timingBpm) && timingBpm is > 0 and <= 400) bpm = Math.Round(timingBpm, 2);
                }
                continue;
            }
            if (section is not ("general" or "metadata")) continue;
            var colon = line.IndexOf(':'); if (colon <= 0) continue;
            var key = line[..colon]; var value = line[(colon + 1)..].Trim();
            if (value.IsEmpty) continue;
            if (section == "general" && key.SequenceEqual("AudioFilename")) audio = value.ToString();
            else if (section == "metadata")
            {
                if (key.SequenceEqual("TitleUnicode")) title = value.ToString();
                else if (key.SequenceEqual("Title")) title ??= value.ToString();
                else if (key.SequenceEqual("ArtistUnicode")) artist = value.ToString();
                else if (key.SequenceEqual("Artist")) artist ??= value.ToString();
                else if (key.SequenceEqual("Creator")) creator = value.ToString();
                else if (key.SequenceEqual("Version")) version = value.ToString();
                else if (key.SequenceEqual("BeatmapID")) beatmap = Id(value);
                else if (key.SequenceEqual("BeatmapSetID")) set = Id(value);
            }
        }
        return new(audio, title, artist, creator, version, beatmap, set, bpm, cover, storyboard);
    }
    private static string? Id(ReadOnlySpan<char> value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number.ToString(CultureInfo.InvariantCulture) : null;
    private static List<string> Csv(ReadOnlySpan<char> line)
    {
        var values = new List<string>(8); var quoted = false; var start = 0;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ',' && !quoted)
            { values.Add(line[start..i].Trim().Trim('"').ToString()); start = i + 1; }
        }
        values.Add(line[start..].Trim().Trim('"').ToString());
        return values;
    }
}
