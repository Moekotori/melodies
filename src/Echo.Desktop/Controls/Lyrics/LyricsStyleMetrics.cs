using Avalonia;
using Avalonia.Media;
using Echo.Contracts.Lyrics;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// CSS metric ports, rather than one shared scale. Sources are named in LYRIC_STYLE_FIDELITY.md.
internal readonly record struct LyricsLineMetrics(double Size, double SecondarySize, double LineHeight,
    double LetterSpacing, FontWeight Weight, double MinimumHeight, double Gap, bool Secondary, double Opacity);

internal static class LyricsStyleMetrics
{
    public static LyricsLineMetrics Line(LyricsLayout layout, LyricLine line, LyricsAppearance a, double w, double h, bool focus, int distance = 0)
    {
        var px = Math.Clamp(a.FontSize, 18, 72);
        var secondary = Math.Clamp(a.SecondarySize, 10, 32);
        var weightedLength = line.Text.Length + Math.Round(((a.Translation ? line.Translation?.Length ?? 0 : 0)
            + (a.Romanization ? line.Romanization?.Length ?? 0 : 0)) * 0.45, MidpointRounding.AwayFromZero);
        var density = weightedLength >= 86 ? 3 : weightedLength >= 58 ? 2 : weightedLength >= 36 ? 1 : 0;
        var size = px * (focus ? density switch { 0 => 1.25, 1 => 1.12, 2 => .96, _ => .84 }
            : density switch { 0 => .9, 1 => .84, 2 => .74, _ => .64 });
        var leading = focus ? 1.18 : 1.12; var tracking = 0d; var weight = 850;
        var row = 0d; var gap = 20d; var opacity = focus ? 1 : .38; var showSecondary = true;
        switch (layout)
        {
            case LyricsLayout.Light:
                size = focus ? density switch
                {
                    1 => Clamp(px * 1.3, w * .05, px * 2.5),
                    2 => Clamp(px * 1.2, w * .042, px * 2.1),
                    3 => Clamp(px * 1.08, w * .0345, px * 1.76),
                    _ => Clamp(px * 1.45, w * .064, px * 3.2)
                } : Clamp(px * .92, w * .0185, px * 1.5);
                leading = focus ? 1.1 : 1.22; weight = focus ? 760 : 640;
                secondary = Clamp(15, w * .015, 26); gap = Clamp(20, h * .034, 40);
                opacity = focus ? 1 : .288; showSecondary = focus; break;
            case LyricsLayout.CoverLeft:
                size = focus ? Clamp(38, Math.Max(w * .041, px * 1.25), 72) : Clamp(26, px * .85, 38);
                if (w <= 760 && focus) size = Clamp(32, w * .066, 46);
                leading = 1.34; weight = focus ? 650 : 520; secondary = Clamp(14, secondary, 20);
                row = Clamp(96, h * .12, 146); gap = 0; opacity = focus ? 1 : .62; break;
            case LyricsLayout.Editorial:
                size = focus ? density switch
                {
                    0 => Math.Min(px * 1.42, Clamp(48, w * .042, 64)),
                    1 => Math.Min(px * 1.16, Clamp(40, w * .036, 52)),
                    _ => Math.Min(px, Clamp(34, w * .03, 44))
                } : density switch
                {
                    0 => Math.Min(px * .92, Clamp(30, w * .024, 40)),
                    1 => Math.Min(px * .8, Clamp(27, w * .021, 34)),
                    _ => Math.Min(px * .7, Clamp(24, w * .019, 31))
                };
                leading = focus ? 1.16 : 1.22; weight = focus ? 720 : 680;
                row = Clamp(focus ? 114 : 106, h * (focus ? .15 : .14), focus ? 144 : 136) * .82;
                gap = Clamp(44, h * .06, 66) * .82; opacity = focus ? 1 : .52; break;
            case LyricsLayout.Folded:
                size = focus ? density switch
                {
                    0 => Math.Min(px * 2.28, Clamp(74, w * .082, 142)),
                    1 => Math.Min(px * 1.78, Clamp(58, w * .065, 108)),
                    _ => Math.Min(px * 1.38, Clamp(44, w * .05, 82))
                } : Clamp(48, w * .058, 100);
                leading = focus ? 1.02 : 1.08; tracking = size * (focus ? -.055 : -.045);
                weight = focus ? 590 : 560; secondary = Clamp(15, w * .0145, 24);
                row = focus ? Clamp(128, h * .20, 220) : Clamp(92, h * .13, 152);
                gap = Clamp(18, h * .028, 34); opacity = focus ? 1 : distance switch { 1 => .38 * .58, 2 => .38 * .4, _ => .38 * .24 }; break;
            case LyricsLayout.Vinyl:
                size = Clamp(20, px * .78, 32); secondary = Clamp(12, secondary * .85, 20);
                leading = 1.5; weight = 500; gap = 16 * .82; opacity = focus ? 1 : .38; break;
            case LyricsLayout.Cinema:
                size = px * (focus ? density switch { 2 => 1.08, 3 => .92, _ => 1.3 } : .84);
                leading = 1.16; tracking = size * -.018; weight = focus ? 760 : 680;
                row = Clamp(76, h * .094, 112); gap = Clamp(10, h * .017, 20);
                opacity = focus ? 1 : .38 * (distance == 1 ? .8 : .5); break;
            case LyricsLayout.CoverRight:
                size = focus ? density switch { 1 => Clamp(32, w * .0218, 43), 2 or 3 => Clamp(29, w * .0195, 38), _ => Clamp(34, w * .0235, 46) }
                    : Clamp(25, w * .018, 34);
                secondary = Clamp(14, w * .0096, 18); leading = focus ? 1.18 : 1.22; weight = focus ? 760 : 620;
                row = Clamp(88, h * .108, 132); gap = Clamp(30, h * .036, 48); opacity = focus ? 1 : .52; break;
            case LyricsLayout.Poster:
                size = focus ? Clamp(44, w * .05, 88) : px * .9; weight = focus ? 640 : 500;
                row = focus ? Clamp(230, h * .31, 340) : Clamp(84, h * .09, 110); gap = Clamp(8, h * .012, 14);
                opacity = focus ? 1 : .10; break;
            case LyricsLayout.WordFocus:
                size = Clamp(px, w * .06, px * 2); leading = 1.35; tracking = size * .025;
                weight = 700; showSecondary = focus; gap = 32; break;
            case LyricsLayout.Slices:
                size = Clamp(38, w * .047, 88); leading = .94; weight = 560; showSecondary = focus; break;
        }
        return new(size, secondary, leading, tracking, (FontWeight)weight, row, gap, showSecondary, opacity);
    }
    public static Rect TextArea(LyricsLayout layout, double w, double h)
    {
        var safe = Clamp(54, w * .07, 148);
        if (layout == LyricsLayout.Light) return new(safe, 0, w - safe * 2, h);
        if (layout == LyricsLayout.Folded) { safe = Clamp(64, w * .08, 168); return new(safe, 0, w - safe * 2, h); }
        if (layout == LyricsLayout.Cinema) { safe = Clamp(56, w * .10, 190); return new(safe, 0, w - safe * 2, h); }
        if (layout == LyricsLayout.CoverLeft && w > 760) { var x = w * .43 + Clamp(16, w * .016, 30); return new(x, 0, w - x - Clamp(42, w * .05, 100), h); }
        if (layout == LyricsLayout.CoverRight && w > 900) { var x = w * .58 + Clamp(130, w * .09, 180); return new(x, 0, Math.Max(100, w - x - Clamp(24, w * .02, 40)), h); }
        if (layout == LyricsLayout.Vinyl && w > 900) { safe = Clamp(48, w * .07, 148); var gap = Clamp(48, w * .06, 112); var available = w - safe * 2 - gap; return new(safe, 0, available * .63, h); }
        if (layout == LyricsLayout.Editorial && w > 780) { var x = Clamp(40, w * .031, 56) + Clamp(250, w * .21, 312) + Clamp(56, w * .052, 84) + Clamp(34, w * .034, 48) + 18; return new(x, 0, Math.Max(100, w - x - Clamp(72, w * .058, 98)), h); }
        return new(w * .08, 0, w * .84, h);
    }
    public static double Clamp(double minimum, double value, double maximum) => Math.Clamp(value, minimum, Math.Max(minimum, maximum));
}
