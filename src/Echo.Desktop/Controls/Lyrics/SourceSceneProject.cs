using System.Text.Json.Serialization;
using Echo.Contracts.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

internal sealed record SourceSceneProject(string Title, string Artist, double DurationMs, SourceSceneLine[] Lines,
    bool Translation, string Font, double FontSize, double SecondarySize, int Seed = 0, string Phase = "auto", bool Retro = true)
{
    [JsonIgnore] public byte[]? Artwork { get; init; }
}
internal sealed record SourceSceneLine(string Text, double TimeMs, double EndTimeMs, LyricWord[] Words, string? Translation, string? Romanization);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SourceSceneProject))]
internal partial class SourceSceneJson : JsonSerializerContext;
internal static class SourceSceneProjectBuilder
{
    // The upstream planners cap at 512 lines / 32768 text characters. Retain the same budget before crossing the native script boundary.
    public static SourceSceneProject Create(LyricsDocument document, string title, string artist, double duration,
        bool translation, string font, double fontSize, double secondarySize)
    {
        var budget = 32768; var lines = new List<SourceSceneLine>();
        foreach (var line in document.Lines.Take(512))
        {
            if (line.Text.Length > budget) break;
            budget -= line.Text.Length;
            lines.Add(new(line.Text, line.StartMs, line.EndMs, line.Words.Take(256).ToArray(),
                translation ? line.Translation : null, line.Romanization));
        }
        return new(title, artist, duration, lines.ToArray(), translation, font == "跟随样式" ? "" : font, fontSize, secondarySize);
    }
}
