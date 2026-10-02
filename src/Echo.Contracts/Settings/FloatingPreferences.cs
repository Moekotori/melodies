namespace Echo.Contracts.Settings;

public sealed record FloatingBounds(int X, int Y, double Width, double Height);
public sealed record FloatingPreferences(bool MiniEnabled = false, bool LyricsEnabled = false,
    bool MiniLocked = false, bool LyricsLocked = false, bool MiniTopmost = true,
    bool DoubleLine = true, bool Translation = true, bool Romanization = false,
    bool HideWithoutLyrics = false, double FontSize = 32, double SecondarySize = 20,
    double Opacity = 1, string FontFamily = "avares://Echo.Desktop/Resources/Fonts#Outfit, Microsoft YaHei, sans-serif",
    string Color = "#FFFFFF", string HighlightColor = "#A8D6FF", double OffsetMs = 0,
    FloatingBounds? MiniBounds = null, FloatingBounds? LyricsBounds = null);
