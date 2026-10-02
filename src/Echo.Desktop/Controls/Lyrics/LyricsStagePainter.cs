using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

internal sealed class LyricsStagePainter
{
    private readonly AfterglowScenePainter scenes = new();
    private readonly IBrush scrim = Brush("#AC03060C");
    private readonly IBrush ivory = Brush("#FFF3D4");
    private readonly Pen groove = new(Brush("#2FFFFFFF"), 1);
    private readonly Pen fold = new(Brush("#3278E4EE"), 1);
    private readonly Pen edge = new(Brush("#35FFFFFF"), 1);
    private LyricsStyle? style;
    private AfterglowLook? look;
    private IBrush background = Brushes.Transparent;
    private IBrush glow = Brushes.Transparent;
    private IBrush secondGlow = Brushes.Transparent;
    private readonly IBrush beam = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = [new(Color.FromArgb(0, 235, 240, 255), 0), new(Color.FromArgb(32, 235, 240, 255), 0.5), new(Color.FromArgb(0, 235, 240, 255), 1)]
    };
    public void Configure(LyricsStyle current, AfterglowLook currentLook, bool dark)
    {
        style = current; look = currentLook;
        var afterglow = current.Layout == LyricsLayout.Afterglow;
        background = Brush(afterglow ? currentLook.Background : current.Background.Length == 0 ? (dark ? "#0C1016" : "#F7F8FA") : current.Background);
        var accent = Color.Parse(afterglow ? currentLook.Accent : current.Accent.Length == 0 ? "#4B55E8" : current.Accent);
        glow = Glow(accent, new RelativePoint(0.22, 0.42, RelativeUnit.Relative));
        secondGlow = Glow(Color.Parse(afterglow ? currentLook.Secondary : "#A174E5"), new RelativePoint(0.84, 0.67, RelativeUnit.Relative));
        scenes.Configure(currentLook);
    }
    public void Draw(DrawingContext dc, Rect bounds, Bitmap? artwork, int line, double phase)
    {
        if (style is null || look is null) return;
        var layout = style.Layout; var w = bounds.Width; var h = bounds.Height;
        dc.DrawRectangle(background, null, bounds);
        if (artwork is not null && layout is not (LyricsLayout.Editorial or LyricsLayout.Afterglow))
        {
            using (dc.PushOpacity(layout == LyricsLayout.Reading ? 0.10 : layout == LyricsLayout.CoverLeft ? 0.12 : 0.22)) Image(dc, artwork, bounds);
            if (layout != LyricsLayout.Reading) dc.DrawRectangle(scrim, null, bounds);
        }
        if (layout is not (LyricsLayout.Reading or LyricsLayout.Editorial))
        { dc.DrawRectangle(glow, null, bounds); dc.DrawRectangle(secondGlow, null, bounds); }
        if (layout == LyricsLayout.Afterglow) { scenes.Draw(dc, bounds, line, phase); return; }
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
                    using (dc.PushOpacity(0.2)) DrawCover(dc, artwork, cover.Translate(new Vector(18, 16)));
                    DrawCover(dc, artwork, cover);
                }
                break;
            case LyricsLayout.CoverRight:
                if (w >= 780)
                {
                    DrawCover(dc, artwork, new Rect(0, 0, w * 0.58, h));
                    var fade = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops = [new(Color.Parse("#0016181E"), 0), new(Color.Parse("#16181E"), 1)]
                    };
                    dc.DrawRectangle(fade, null, new Rect(w * 0.38, 0, w * 0.25, h));
                }
                break;
            case LyricsLayout.Vinyl:
                if (w >= 780)
                {
                    var center = new Point(w * 0.80, h * 0.47); var radius = Math.Min(w * 0.16, h * 0.33);
                    dc.DrawEllipse(Brush("#121014"), edge, center, radius, radius);
                    for (var i = 1; i < 11; i++) dc.DrawEllipse(null, groove, center, radius * (0.36 + i * 0.055), radius * (0.36 + i * 0.055));
                    using (dc.PushGeometryClip(new EllipseGeometry(new Rect(center.X - radius * 0.32, center.Y - radius * 0.32, radius * 0.64, radius * 0.64))))
                        DrawCover(dc, artwork, new Rect(center.X - radius * 0.32, center.Y - radius * 0.32, radius * 0.64, radius * 0.64));
                    dc.DrawEllipse(background, null, center, radius * 0.04, radius * 0.04);
                }
                break;
            case LyricsLayout.Folded:
                for (var i = 0; i < 5; i++)
                {
                    var x = w * (0.12 + i * 0.17);
                    Polygon(dc, [new(x, 0), new(x + w * 0.16, h * 0.4), new(x - w * 0.02, h), new(x - w * 0.10, h * 0.50)], null, fold);
                }
                break;
            case LyricsLayout.Cinema:
                dc.DrawRectangle(background, null, new Rect(0, 0, w, h * 0.1));
                dc.DrawRectangle(background, null, new Rect(0, h * 0.88, w, h * 0.12));
                dc.DrawLine(edge, new(30, h * 0.12), new(w - 30, h * 0.12));
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
                    for (var i = 0; i < 4; i++)
                    {
                        var x = w * (0.5 + i * 0.12);
                        var points = new Point[] { new(x, h * 0.15), new(x + w * 0.1, h * 0.1), new(x - w * 0.07, h * 0.76), new(x - w * 0.17, h * 0.81) };
                        using (dc.PushGeometryClip(Geometry(points)))
                        using (dc.PushOpacity(0.5)) Image(dc, artwork, bounds.Translate(new Vector(i * 22, 0)));
                        Polygon(dc, points, null, edge);
                    }
                dc.DrawLine(edge, new(0, h * 0.82), new(w, h * 0.82));
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
