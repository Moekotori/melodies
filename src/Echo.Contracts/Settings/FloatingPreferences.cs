namespace Echo.Contracts.Settings;

public static class DesktopLyricsFont
{
    public const string Default = "avares://Echo.Desktop/Resources/Fonts#Outfit, Microsoft YaHei UI, PingFang SC, Noto Sans SC, Microsoft YaHei, sans-serif";
}
public sealed record FloatingBounds(int X, int Y, double Width, double Height);
public sealed record FloatingPreferences(bool MiniEnabled = false, bool LyricsEnabled = false,
    bool MiniLocked = false, bool LyricsLocked = false, bool MiniTopmost = true,
    bool DoubleLine = true, bool Translation = true, bool Romanization = true,
    bool HideWithoutLyrics = false, double FontSize = 34, double SecondarySize = 19,
    double Opacity = 0.96, string FontFamily = DesktopLyricsFont.Default,
    string Color = "#FFFFFF", string HighlightColor = "#A8D6FF", double OffsetMs = 0,
    FloatingBounds? MiniBounds = null, FloatingBounds? LyricsBounds = null,
    double ScalePercent = 100, string ColorMode = "theme", string StrokeColor = "#111827",
    string GradientStart = "#4F46E5", string GradientEnd = "#EC4899",
    string TextDirection = "horizontal", string TextAlign = "center",
    bool Interpolated = true, bool DropShadow = true, bool StrokeEnabled = true, bool IndependentRows = false,
    string UnplayedTop = "#FFFFFF", string UnplayedBottom = "#CCCCCC",
    string OutlinePlayed = "#111827", string OutlineUnplayed = "#111827",
    string LowerPlayedTop = "#FFFFFF", string LowerPlayedBottom = "#FFFFFF",
    string LowerUnplayedTop = "#FFFFFF", string LowerUnplayedBottom = "#CCCCCC",
    string LowerOutlinePlayed = "#111827", string LowerOutlineUnplayed = "#111827",
    string FontFilePath = "");
