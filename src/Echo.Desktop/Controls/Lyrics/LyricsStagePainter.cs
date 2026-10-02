using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Controls.Lyrics;

internal sealed class LyricsStagePainter
{
    private readonly IBrush scrim = Brush("#AC03060C");
    private readonly IBrush ivory = Brush("#FFF3D4");
    private readonly Pen edge = new(Brush("#35FFFFFF"), 1);
    private LyricsStyle? style;
    private AfterglowLook? look;
    private IBrush background = Brushes.Transparent;
    private IBrush glow = Brushes.Transparent;
    private IBrush secondGlow = Brushes.Transparent;
    private LyricsStagePalette palette;
    private readonly BlurEffect coverBlur = new() { Radius = 10 };
    private readonly IBrush beam = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = [new(Color.FromArgb(0, 235, 240, 255), 0), new(Color.FromArgb(32, 235, 240, 255), 0.5), new(Color.FromArgb(0, 235, 240, 255), 1)]
    };
    public void Configure(LyricsStyle current, AfterglowLook currentLook, bool dark, ArtworkColorSample? colors = null)
    {
        style = current; look = currentLook;
        var afterglow = current.Layout == LyricsLayout.Afterglow;
        palette = LyricsStagePalette.Resolve(current, currentLook, colors, dark);
        background = new SolidColorBrush(palette.Ground);
        var accent = Color.Parse(afterglow ? currentLook.Accent : current.Accent.Length == 0 ? "#4B55E8" : current.Accent);
        glow = Glow(current.Layout == LyricsLayout.CoverLeft ? palette.Wash : accent, new RelativePoint(0.22, 0.42, RelativeUnit.Relative));
        secondGlow = current.Layout is LyricsLayout.CoverLeft or LyricsLayout.WordFocus ? Brushes.Transparent
            : Glow(Color.Parse(afterglow ? currentLook.Secondary : "#A174E5"), new RelativePoint(0.84, 0.67, RelativeUnit.Relative));
    }
    public void Draw(DrawingContext dc, Rect bounds, Bitmap? artwork, int line, double phase)
    {
        if (style is null || look is null) return;
        var layout = style.Layout; var w = bounds.Width; var h = bounds.Height;
        dc.DrawRectangle(background, null, bounds);
        if (artwork is not null && layout is not (LyricsLayout.Editorial or LyricsLayout.Afterglow or LyricsLayout.CoverLeft or LyricsLayout.CoverRight))
        {
            using (dc.PushOpacity(layout == LyricsLayout.Vinyl ? .48 : layout == LyricsLayout.Folded ? .32 : .46))
            using (dc.PushEffect(coverBlur, bounds.Inflate(20))) Image(dc, artwork, bounds.Inflate(20));
            if (layout is not (LyricsLayout.Reading or LyricsLayout.Vinyl)) dc.DrawRectangle(scrim, null, bounds);
        }
        if (layout is not (LyricsLayout.Reading or LyricsLayout.Editorial or LyricsLayout.Vinyl))
        { dc.DrawRectangle(glow, null, bounds); dc.DrawRectangle(secondGlow, null, bounds); }
        // The independent native scene layer owns JIZURA; this painter only supplies its empty backdrop.
        if (layout == LyricsLayout.Afterglow) return;
        switch (layout)
        {
            case LyricsLayout.Light:
                using (dc.PushTransform(Matrix.CreateRotation(-0.3) * Matrix.CreateTranslation(w * 0.12 + phase, -h * 0.2)))
                    dc.DrawRectangle(beam, null, new Rect(0, 0, w * 0.34, h * 1.6));
                using (dc.PushTransform(Matrix.CreateRotation(0.22) * Matrix.CreateTranslation(w * 0.78 - phase, -h * 0.15)))
                    dc.DrawRectangle(beam, null, new Rect(0, 0, w * 0.20, h * 1.6));
                if (artwork is not null) Image(dc, artwork, new Rect(Math.Clamp(w * 0.07, 54, 148), h - Math.Clamp(h * 0.13, 96, 140) - 50, 54, 54));
                break;
            case LyricsLayout.CoverLeft:
                var square = Math.Min(w * 0.36, h * 0.57);
                if (w >= 780)
                {
                    var cover = new Rect(Math.Clamp(w * 0.048, 36, 86), h * 0.45 - square / 2, square, square);
                    DrawCover(dc, artwork, cover);
                }
                break;
            case LyricsLayout.CoverRight:
                if (w >= 780)
                {
                    var coverBounds = new Rect(0, 0, w * .72, h);
                    var mask = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops = [new(Colors.White, 0), new(Colors.White, .42), new(Color.Parse("#DBFFFFFF"), .54),
                            new(Color.Parse("#85FFFFFF"), .68), new(Color.Parse("#33FFFFFF"), .82), new(Color.Parse("#0FFFFFFF"), .92), new(Colors.Transparent, 1)]
                    };
                    if (artwork is not null)
                    {
                        using (dc.PushOpacity(.5)) using (dc.PushEffect(coverBlur, bounds)) Image(dc, artwork, bounds);
                        using (dc.PushOpacityMask(mask, coverBounds)) Image(dc, artwork, coverBounds);
                    }
                }
                break;
            case LyricsLayout.Vinyl:
                if (w > 900)
                {
                    var safe = Math.Clamp(w * .07, 48, 148); var gap = Math.Clamp(w * .06, 48, 112);
                    var side = Math.Min(420, (w - safe * 2 - gap) * .37);
                    DrawCover(dc, artwork, new Rect(w - safe - side, h * .48 - side / 2, side, side));
                }
                break;
            case LyricsLayout.Folded:
                break;
            case LyricsLayout.Cinema:
                var barHeight = Math.Clamp(h * .025, 15, 28);
                dc.DrawRectangle(Brush("#D1000205"), null, new Rect(0, 0, w, barHeight));
                dc.DrawRectangle(Brush("#D1000205"), null, new Rect(0, h - barHeight, w, barHeight));
                using (dc.PushOpacity(.55)) Label(dc, "ECHO / CINEMA", w - 144, Math.Clamp(h * .044, 28, 48), 10, ivory);
                break;
            case LyricsLayout.Poster:
                var bar = Brush(style.Accent);
                using (dc.PushOpacity(0.45))
                {
                    foreach (var point in new[] { new Point(24, 84), new Point(w - 48, 84), new Point(24, h - 150), new Point(w - 48, h - 150) })
                    { dc.DrawLine(new Pen(bar), point, point + new Vector(24, 0)); dc.DrawLine(new Pen(bar), point, point + new Vector(0, 24)); }
                    Label(dc, "ECHO", 26, h - 183, 18, bar);
                }
                break;
            case LyricsLayout.Slices:
                if (artwork is not null)
                    for (var i = 0; i < 7; i++)
                    {
                        double[] top = [.118, .21, .321, .446, .556, .667, .744, .902], bottom = [.059, .088, .172, .378, .488, .582, .703, .913];
                        var points = new Point[] { new(w * top[i], 0), new(w * top[i + 1], 0), new(w * bottom[i + 1], h * .72), new(w * bottom[i], h * .72) };
                        using (dc.PushGeometryClip(Geometry(points)))
                        using (dc.PushOpacity(.84)) Image(dc, artwork, bounds);
                        Polygon(dc, points, null, edge);
                    }
                dc.DrawLine(edge, new(0, h * .72), new(w, h * .72));
                break;
            case LyricsLayout.Editorial:
                if (w >= 780)
                {
                    var paperCover = Math.Clamp(w * 0.21, 250, 312);
                    DrawCover(dc, artwork, new Rect(Math.Clamp(w * 0.031, 40, 56), h * 0.25, paperCover, paperCover));
                }
                break;
        }
    }
    private void DrawCover(DrawingContext dc, Bitmap? image, Rect rect)
    {
        if (image is not null)
        {
            using (dc.PushClip(new RoundedRect(rect, style?.Layout is LyricsLayout.CoverLeft or LyricsLayout.Editorial ? 12 : 0))) Image(dc, image, rect);
            return;
        }
        dc.DrawRectangle(glow, edge, rect);
        Label(dc, "ECHO", rect.X + rect.Width * 0.16, rect.Y + rect.Height * 0.43, Math.Min(34, rect.Width * 0.17), ivory);
    }
    internal static void Image(DrawingContext dc, Bitmap image, Rect target)
    {
        var size = image.Size; var scale = Math.Max(target.Width / size.Width, target.Height / size.Height);
        var source = new Rect((size.Width - target.Width / scale) / 2, (size.Height - target.Height / scale) / 2, target.Width / scale, target.Height / scale);
        dc.DrawImage(image, source, target);
    }
    internal static void Label(DrawingContext dc, string text, double x, double y, double size, IBrush brush)
        => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei, Noto Sans CJK SC"), size, brush), new Point(x, y));
    internal static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static IBrush Glow(Color color, RelativePoint center) => new RadialGradientBrush
    {
        Center = center, GradientOrigin = center, RadiusX = new(0.7, RelativeUnit.Relative), RadiusY = new(0.7, RelativeUnit.Relative),
        GradientStops = [new GradientStop(Color.FromArgb(48, color.R, color.G, color.B), 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1)]
    };
    internal static StreamGeometry Geometry(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry(); using var path = geometry.Open();
        path.BeginFigure(points[0], true); foreach (var point in points.Skip(1)) path.LineTo(point); path.EndFigure(true);
        return geometry;
    }
    internal static void Polygon(DrawingContext dc, IReadOnlyList<Point> points, IBrush? brush, IPen? pen)
        => dc.DrawGeometry(brush, pen, Geometry(points));
}
