using Avalonia.Media;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

internal readonly record struct LyricsStagePalette(Color Ground, Color Ink, Color Accent, Color Wash)
{
    public static LyricsStagePalette Resolve(LyricsStyle style, AfterglowLook look, ArtworkColorSample? sample, bool dark)
    {
        if (style.Layout == LyricsLayout.Afterglow) return new(Color.Parse(look.Background), Color.Parse(look.Ink), Color.Parse(look.Accent), Color.Parse(look.Secondary));
        var ink = Color.Parse(style.Ink.Length == 0 ? dark ? "#EBEFF5" : "#292E37" : style.Ink);
        var accent = Color.Parse(style.Accent.Length == 0 ? dark ? "#A8D6FF" : "#4B55E8" : style.Accent);
        var ground = Color.Parse(style.Background.Length == 0 ? dark ? "#0C1016" : "#F7F8FA" : style.Background);
        var source = sample?.Dominant ?? Color.Parse("#ECB677");
        var wash = source;
        if (style.Layout == LyricsLayout.CoverLeft)
        {
            ground = Mix(source, Color.Parse("#090A0D"), sample is null ? .92 : .88);
            wash = Mix(source, Color.Parse("#090A0D"), .52);
            var chroma = Math.Max(source.R, Math.Max(source.G, source.B)) - Math.Min(source.R, Math.Min(source.G, source.B));
            accent = Mix(chroma < 22 ? Color.Parse("#ECB677") : source, Color.Parse("#F9F5ED"), .43);
            if (Contrast(accent, ground) < 4.5) accent = Mix(accent, Color.Parse("#F9F5ED"), .52);
        }
        if (style.Layout is LyricsLayout.Light or LyricsLayout.Folded or LyricsLayout.Cinema or LyricsLayout.CoverRight) accent = ink;
        if (style.Layout == LyricsLayout.CoverRight && sample is not null) ground = Mix(sample.Average, Color.Parse("#121418"), .72);
        if (style.Layout == LyricsLayout.Vinyl)
        {
            ink = sample is not null && Luminance(sample.Average) > .56 ? Color.Parse("#1B2A3D") : Color.Parse("#F8FAFC");
            accent = ink;
        }
        return new(ground, ink, accent, wash);
    }
    public static Color Mix(Color a, Color b, double ratio) => Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * ratio),
        (byte)Math.Round(a.G + (b.G - a.G) * ratio), (byte)Math.Round(a.B + (b.B - a.B) * ratio));
    private static double Contrast(Color a, Color b) { var x = Luminance(a); var y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
    private static double Luminance(Color c)
    {
        static double Channel(byte b) { var x = b / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
        return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
    }
}
