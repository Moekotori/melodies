using Avalonia;
using Avalonia.Media;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// Gradient stacks and cover filters copied from the Electron lyrics stylesheets.
internal sealed class LyricsStageAtmosphere
{
    private readonly List<IBrush> veils = [];
    public double Blur { get; private set; }
    public double WashOpacity { get; private set; }
    public double Brightness { get; private set; } = 1;
    public double Saturate { get; private set; } = 1;
    public double Contrast { get; private set; } = 1;
    public IBrush? DriftPrimary { get; private set; }
    public IBrush? DriftCounter { get; private set; }
    public IBrush? DriftGlowA { get; private set; }
    public IBrush? DriftGlowB { get; private set; }
    public IReadOnlyList<IBrush> Veils => veils;

    public void Configure(LyricsStyle style, LyricsStagePalette palette, bool dark)
    {
        veils.Clear();
        DriftPrimary = DriftCounter = DriftGlowA = DriftGlowB = null;
        Blur = 0;
        WashOpacity = 0;
        Brightness = Saturate = Contrast = 1;
        var accent = style.Accent.Length == 0 ? Color.Parse("#96A8FF") : Color.Parse(style.Accent);
        switch (style.Layout)
        {
            case LyricsLayout.Reading:
                Cover(10, 1, dark ? 1 : .82, dark ? 1.08 : .94);
                break;
            case LyricsLayout.Light:
                Cover(10, 1, 1, 1.08);
                var deep = LyricsStagePalette.Mix(accent, Colors.White, .26);
                var soft = LyricsStagePalette.Mix(accent, Colors.White, .54);
                if (dark)
                {
                    veils.Add(Radial(.32, .52, .92, .78, (0, Transparent(0)), (1, Argb(.46, 7, 9, 17))));
                    veils.Add(Vertical((0, Argb(.52, 7, 9, 17)), (.42, Argb(.3, 7, 9, 17)), (.7, Argb(.4, 7, 9, 17)), (1, Argb(.74, 7, 9, 17))));
                }
                else
                {
                    veils.Add(Radial(.32, .52, .92, .78, (0, Argb(.34, 7, 9, 17)), (1, Argb(.66, 7, 9, 17))));
                    veils.Add(Vertical((0, Argb(.6, 7, 9, 17)), (.48, Argb(.44, 7, 9, 17)), (1, Argb(.8, 7, 9, 17))));
                }
                DriftPrimary = Linear(false, (0, Transparent(0)), (.26, Transparent(0)), (.41, Alpha(.3, deep)), (.49, Argb(.18, 255, 255, 255)), (.57, Alpha(.26, soft)), (.72, Transparent(0)), (1, Transparent(0)));
                DriftCounter = Linear(false, (.32, Transparent(0)), (.46, Alpha(.1, soft)), (.5, Argb(.06, 255, 255, 255)), (.54, Alpha(.1, deep)), (.68, Transparent(0)));
                DriftGlowA = Radial(.84, .16, .34, .48, (0, Alpha(.36, deep)), (.72, Transparent(0)));
                DriftGlowB = Radial(.1, .9, .44, .38, (0, Alpha(.22, soft)), (.74, Transparent(0)));
                break;
            case LyricsLayout.CoverLeft:
                Cover(10, .18, 1, 1.08);
                veils.Add(Radial(.22, .42, .9, .75, (0, Alpha(.38, palette.Wash)), (.64, Transparent(0))));
                break;
            case LyricsLayout.Editorial:
                veils.Add(Radial(.22, .4, 1, 1, (0, Argb(.055, 111, 116, 255)), (.32, Transparent(0))));
                veils.Add(Radial(.78, .2, 1, 1, (0, Argb(.08, 165, 171, 226)), (.3, Transparent(0))));
                veils.Add(Vertical((0, Color.Parse("#FDFcfb")), (.58, Color.Parse("#FAF9F7")), (1, Color.Parse("#F7F6F4"))));
                break;
            case LyricsLayout.Folded:
                Cover(0, 1, 1);
                var violet = Color.Parse("#B693EB");
                veils.Add(Radial(.16, .22, .86, .7, (0, Alpha(.29, accent)), (.46, Alpha(.09, accent)), (.74, Transparent(0))));
                veils.Add(Radial(.84, .66, .82, .72, (0, Alpha(.24, violet)), (.48, Alpha(.08, violet)), (.76, Transparent(0))));
                veils.Add(Horizontal((0, Argb(.74, 0, 3, 8)), (.46, Argb(.32, 0, 4, 9)), (1, Argb(.62, 0, 3, 8))));
                veils.Add(Vertical((0, Argb(.28, 0, 4, 9)), (.54, Argb(.52, 0, 4, 9)), (1, Argb(.82, 0, 3, 8))));
                break;
            case LyricsLayout.Vinyl:
                Cover(18, 1, .46, 1.05);
                veils.Add(Radial(.07, .06, .33, .33, (0, Alpha(.48, accent)), (1, Transparent(0))));
                veils.Add(Radial(.25, .48, .44, .44, (0, Alpha(.32, accent)), (1, Transparent(0))));
                veils.Add(Vertical((0, Transparent(0)), (.76, Argb(.66, 8, 7, 11)), (1, Argb(.86, 4, 5, 7))));
                break;
            case LyricsLayout.Cinema:
                Cover(0, 1, .52, .9, 1.06);
                veils.Add(Radial(.5, .48, .78, .58, (0, Transparent(0)), (.4, Transparent(0)), (.72, Argb(.3, 0, 3, 8)), (1, Argb(.7, 0, 2, 6))));
                veils.Add(Horizontal((0, Argb(.46, 0, 3, 8)), (.24, Transparent(0)), (.76, Transparent(0)), (1, Argb(.46, 0, 3, 8))));
                veils.Add(Vertical((0, Argb(.36, 0, 2, 6)), (.3, Transparent(0)), (.66, Transparent(0)), (1, Argb(.62, 0, 2, 6))));
                veils.Add(Radial(.22, .32, .58, .42, (0, Alpha(.17, accent)), (.72, Transparent(0))));
                veils.Add(Radial(.82, .66, .52, .38, (0, Argb(.1, 118, 170, 200)), (.74, Transparent(0))));
                break;
            case LyricsLayout.Poster:
                Cover(0, 1, .68, .9, 1.04);
                veils.Add(Radial(.5, .5, .54, .42, (0, Argb(.66, 3, 10, 21)), (.86, Transparent(0))));
                veils.Add(Horizontal((0, Argb(.76, 3, 10, 21)), (.37, Argb(.34, 7, 16, 30)), (.68, Argb(.12, 5, 13, 25)), (1, Argb(.34, 5, 14, 28))));
                veils.Add(Vertical((0, Argb(.18, 5, 11, 20)), (.54, Argb(.1, 4, 13, 25)), (1, Argb(.78, 3, 12, 25))));
                veils.Add(Radial(.34, .22, .72, .7, (0, Alpha(.22, accent)), (.7, Transparent(0))));
                break;
            case LyricsLayout.CoverRight:
                Cover(40, .92, .78, 1.4, 1.05);
                veils.Add(Horizontal((0, Transparent(0)), (.32, Transparent(0)), (.42, Alpha(.04, palette.Wash)), (.54, Alpha(.12, palette.Wash)), (.72, Alpha(.22, palette.Ground)), (1, Alpha(.3, palette.Ground))));
                veils.Add(Vertical((.42, Transparent(0)), (1, Alpha(.08, palette.Ground))));
                break;
            case LyricsLayout.Slices:
                Cover(0, 1, .3, .68, 1.1);
                veils.Add(Horizontal((0, Argb(.92, 5, 8, 12)), (.06, Argb(.92, 5, 8, 12)), (.15, Transparent(0)), (.84, Transparent(0)), (.95, Argb(.9, 5, 8, 12)), (1, Argb(.9, 5, 8, 12))));
                veils.Add(Vertical((0, Argb(.04, 4, 8, 12)), (.48, Argb(.04, 4, 8, 12)), (.74, Argb(.88, 4, 8, 12)), (1, Argb(.88, 4, 8, 12))));
                break;
            case LyricsLayout.CoverOnly:
                Cover(36, .18, 1);
                break;
            case LyricsLayout.WordFocus:
            case LyricsLayout.Afterglow:
                break;
        }
    }

    private void Cover(double blur, double opacity, double brightness, double saturate = 1, double contrast = 1)
    {
        Blur = blur;
        WashOpacity = opacity;
        Brightness = brightness;
        Saturate = saturate;
        Contrast = contrast;
    }

    public static Matrix Drift(Rect sheet, double ms, double period, double x0, double y0, double r0, double x1, double y1, double r1, double xr, double yr, double rr)
    {
        double x, y, rot;
        if (ms < 0) { x = xr; y = yr; rot = rr; }
        else
        {
            var p = Alternate(ms, period);
            x = x0 + (x1 - x0) * p;
            y = y0 + (y1 - y0) * p;
            rot = r0 + (r1 - r0) * p;
        }
        var center = sheet.Center;
        return Matrix.CreateTranslation(-center.X, -center.Y)
            * Matrix.CreateRotation(rot * Math.PI / 180)
            * Matrix.CreateTranslation(center.X + sheet.Width * x, center.Y + sheet.Height * y);
    }

    private static double Alternate(double ms, double period)
    {
        var u = ms / period % 2;
        if (u < 0) u += 2;
        var t = u <= 1 ? u : 2 - u;
        return t < .5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
    }

    private static Color Argb(double alpha, byte r, byte g, byte b) => Color.FromArgb((byte)(alpha * 255), r, g, b);
    private static Color Alpha(double alpha, Color color) => Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B);
    private static Color Transparent(byte _) => Colors.Transparent;
    private static LinearGradientBrush Horizontal(params (double Offset, Color Color)[] stops) => Linear(false, stops);
    private static LinearGradientBrush Vertical(params (double Offset, Color Color)[] stops) => Linear(true, stops);
    private static LinearGradientBrush Linear(bool vertical, params (double Offset, Color Color)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(vertical ? 0 : 1, vertical ? 1 : 0, RelativeUnit.Relative)
        };
        foreach (var stop in stops) brush.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
        return brush;
    }
    private static RadialGradientBrush Radial(double x, double y, double rx, double ry, params (double Offset, Color Color)[] stops)
    {
        var at = new RelativePoint(x, y, RelativeUnit.Relative);
        var brush = new RadialGradientBrush
        {
            Center = at,
            GradientOrigin = at,
            RadiusX = new RelativeScalar(rx, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(ry, RelativeUnit.Relative)
        };
        foreach (var stop in stops) brush.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
        return brush;
    }
}
