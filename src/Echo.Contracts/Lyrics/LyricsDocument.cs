namespace Echo.Contracts.Lyrics;

public sealed record LyricWord(string Text, double StartMs, double EndMs);
public sealed record LyricLine(string Text, double StartMs, double EndMs,
    IReadOnlyList<LyricWord> Words, string? Translation = null, string? Romanization = null);
public sealed record LyricsDocument(IReadOnlyList<LyricLine> Lines, bool IsSynced, double OffsetMs = 0)
{
    public static LyricsDocument Empty { get; } = new([], false);
    public int FindLine(double positionMs)
    {
        if (!IsSynced || Lines.Count == 0) return -1;
        var low = 0; var high = Lines.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (Lines[middle].StartMs <= positionMs + OffsetMs) low = middle + 1;
            else high = middle - 1;
        }
        return high;
    }
}
public sealed record LyricsTrackText(string Text, string Format);
public sealed record LyricsText(string Text, string Format, string Origin,
    LyricsTrackText? Translation = null, LyricsTrackText? Romanization = null);
public interface ILyricsSource
{
    Task<LyricsText?> ReadAsync(string audioPath, string? explicitPath, CancellationToken cancellation);
}
public sealed record LyricsPreferences(string Style = "default", string Look = "mist", double FontSize = 36,
    double SecondarySize = 18, bool Translation = true, bool Romanization = true, bool WordHighlight = true,
    string Alignment = "left", string Font = "", double OffsetMs = 0, bool Context = true,
    bool MiniPlayer = true, bool PlayerAutoHide = true, bool PlayerCompact = true,
    string BackgroundMode = "page", double BackgroundBlur = 18, double BackgroundBrightness = 100,
    double BackgroundOpacity = 100, string WallpaperPath = "");
public interface ILyricsPreferencesStore
{
    Task<LyricsPreferences> ReadAsync(CancellationToken cancellation);
    void Save(LyricsPreferences preferences);
}
