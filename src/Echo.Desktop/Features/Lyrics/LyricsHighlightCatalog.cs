namespace Echo.Desktop.Features.Lyrics;

// Stable ECHO IDs; presentation scenes that own their glyphs retain their own highlight.
public static class LyricsHighlightCatalog
{
    public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(new[]
    {
        "sweep", "fade", "lift", "hop", "swell", "halo", "spot", "wave", "lens",
        "flip", "spark", "scan", "underline", "dots"
    });
    public static string Normalize(string id) => Ids.Contains(id) ? id : "sweep";
    public static string Resolve(string id, LyricsLayout layout) => layout is
        LyricsLayout.Afterglow or LyricsLayout.WordFocus or LyricsLayout.SourceScene or LyricsLayout.Curtain or
        LyricsLayout.Poster or LyricsLayout.Slices or LyricsLayout.CoverOnly ? "sweep" : Normalize(id);
}
