using Echo.Contracts.Lyrics;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

internal static class LyricsWordProgress
{
    public static double Value(LyricWord word, double position, bool estimated, bool soft, bool continuous = true)
    {
        if (position < word.StartMs - (estimated && soft ? 100 : 0)) return 0;
        if (!continuous) return 1;
        if (estimated && soft)
        {
            var t = Math.Clamp((position - word.StartMs + 100) / 150, 0, 1);
            return 1 - Math.Pow(1 - t, 3);
        }
        var duration = word.EndMs - word.StartMs;
        if (!estimated && duration < 150) return position >= word.StartMs ? 1 : 0;
        var p = Math.Clamp((position - word.StartMs) / Math.Max(1, duration), 0, 1);
        // ECHO softenApproximateWordProgress: approximate timing must not imply a precise phoneme edge.
        return estimated ? p * p * (3 - 2 * p) : p;
    }
    public static bool Continuous(LyricsLayout layout) => layout is not (LyricsLayout.Folded or LyricsLayout.Poster or LyricsLayout.Slices);
    public static double Upcoming(LyricsLayout layout) => layout switch
    { LyricsLayout.Light => .46, LyricsLayout.CoverLeft => .42, LyricsLayout.CoverRight => .56, _ => 1 };
    public static double SungFraction(LyricLine line, double position, bool soft, bool continuous)
    {
        double sung = 0, total = 0;
        foreach (var word in line.Words)
        {
            var weight = 0;
            foreach (var rune in word.Text.EnumerateRunes()) if (!System.Text.Rune.IsWhiteSpace(rune)) weight++;
            weight = Math.Max(1, weight); total += weight;
            sung += weight * Value(word, position, line.EstimatedTiming, soft, continuous);
        }
        return total > 0 ? Math.Clamp(sung / total, 0, 1) : 0;
    }
}
