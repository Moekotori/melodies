namespace Echo.Contracts.Lyrics;

// One catalog for discovery, remote sidecars and the import dialog. JSON is deliberately schema-specific.
public static class LyricsFileFormats
{
    public static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(new[]
    {
        ".ttml", ".yrc", ".qrc", ".krc", ".lrcx", ".lrc", ".trc", ".snc",
        ".srt", ".vtt", ".ass", ".ssa", ".echo-lyrics.json", ".txt"
    });
    public static IReadOnlyList<string> DialogPatterns { get; } =
        Array.AsReadOnly(Extensions.Select(extension => "*" + extension).ToArray());
}
