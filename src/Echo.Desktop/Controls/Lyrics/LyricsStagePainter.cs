using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Controls.Lyrics;

internal sealed class LyricsStagePainter : IDisposable
{
    private static readonly double[] SliceTop = [.118, .21, .321, .446, .556, .667, .744, .902];
    private static readonly double[] SliceBottom = [.059, .088, .172, .378, .488, .582, .703, .913];
    private readonly LyricsCoverWash wash = new();
    private readonly LyricsCoverWash sliceWash = new();
    private readonly LyricsStageAtmosphere atmosphere = new();
    private readonly StreamGeometry[] slices = new StreamGeometry[7];
    private readonly Pen sliceEdge = new(Brush(Color.FromArgb(53, 255, 255, 255)), 1);
    private readonly Pen cornerPen = new(Brush(Color.FromArgb(184, 232, 241, 248)), 1);
    private readonly Pen coverRim = new(Brush(Color.FromArgb(31, 249, 245, 237)), 1);
    private readonly IBrush letterbox = Brush(Color.FromArgb(209, 0, 2, 5));
    private readonly IBrush shadeBrush = Brush(Color.FromArgb(41, 2, 6, 10));
    private readonly Pen paperEdge = new(Brush(Color.FromArgb(23, 40, 48, 76)), 1);
    private readonly IBrush coverShadow = new RadialGradientBrush
    {
        Center = new RelativePoint(.5, .58, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(.5, .42, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(.5, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(.5, RelativeUnit.Relative),
        GradientStops = [new(Color.FromArgb(87, 0, 0, 0), 0), new(Color.FromArgb(28, 0, 0, 0), .55), new(Colors.Transparent, 1)]
    };
    private readonly IBrush paperShadow = new RadialGradientBrush
    {
        Center = new RelativePoint(.5, .55, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(.5, .4, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(.5, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(.5, RelativeUnit.Relative),
        GradientStops = [new(Color.FromArgb(36, 41, 48, 72), 0), new(Colors.Transparent, 1)]
    };
    private readonly IBrush sleeveShadow = new RadialGradientBrush
    {
        Center = new RelativePoint(.5, .55, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(.5, .4, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(.5, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(.5, RelativeUnit.Relative),
        GradientStops = [new(Color.FromArgb(26, 20, 24, 32), 0), new(Colors.Transparent, 1)]
    };
    private readonly IBrush cinemaInk = Brush(Color.FromArgb(122, 238, 244, 248));
    private readonly IBrush posterInk = Brush(Color.FromArgb(210, 239, 246, 252));
    private LyricsStyle? style;
    private IBrush background = Brushes.Transparent;
    private IBrush placeholder = Brushes.Transparent;
    private IBrush sliceAccent = Brushes.Transparent;
    private TextLayout? cinemaLabel, posterLabel;
    private double labelHeight, labelWidth, sliceWidth, sliceHeight;

    public void Configure(LyricsStyle current, AfterglowLook currentLook, bool dark, ArtworkColorSample? colors = null)
    {
        style = current;
        var palette = LyricsStagePalette.Resolve(current, currentLook, colors, dark);
        background = Brush(palette.Ground);
        placeholder = Brush(Color.FromArgb(40, palette.Wash.R, palette.Wash.G, palette.Wash.B));
        var tint = current.Accent.Length == 0 ? Color.Parse("#FF705E") : Color.Parse(current.Accent);
        sliceAccent = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new(Color.FromArgb(158, tint.R, tint.G, tint.B), 0), new(Color.FromArgb(138, tint.R, tint.G, tint.B), 1)]
        };
        atmosphere.Configure(current, palette, dark);
    }

    public void Draw(DrawingContext dc, Rect bounds, Bitmap? artwork, double phase)
    {
        if (style is null || bounds.Width < 1 || bounds.Height < 1) return;
        var layout = style.Layout;
        dc.DrawRectangle(background, null, bounds);
        if (artwork is not null && atmosphere.WashOpacity > 0 && layout != LyricsLayout.CoverRight)
        {
            wash.Prepare(artwork, bounds.Width, bounds.Height, atmosphere.Blur, atmosphere.Brightness, atmosphere.Saturate, atmosphere.Contrast);
            if (wash.Image is { } soft)
            {
                var dest = layout == LyricsLayout.Cinema ? Inflate(bounds, .0125) : bounds;
                using (dc.PushOpacity(atmosphere.WashOpacity))
                    dc.DrawImage(soft, new Rect(soft.Size), dest);
            }
        }
        if (layout != LyricsLayout.CoverRight)
            foreach (var veil in atmosphere.Veils) dc.DrawRectangle(veil, null, bounds);
        if (layout == LyricsLayout.Light) DrawDrift(dc, bounds, phase);
        if (layout == LyricsLayout.Afterglow) return;
        switch (layout)
        {
            case LyricsLayout.Light:
                DrawCover(dc, artwork, LyricsStyleMetrics.LuminousCover(bounds.Width, bounds.Height), 2, CoverFrame.Plain);
                break;
            case LyricsLayout.CoverLeft:
                DrawCover(dc, artwork, LyricsStyleMetrics.AfterglowCover(bounds.Width, bounds.Height), 12, CoverFrame.Rim);
                break;
            case LyricsLayout.CoverRight:
                DrawCoverStage(dc, bounds, artwork);
                break;
            case LyricsLayout.Vinyl:
                if (bounds.Width > 900)
                {
                    var safe = Math.Clamp(bounds.Width * .07, 48, 148);
                    var gap = Math.Clamp(bounds.Width * .06, 48, 112);
                    var side = Math.Min(420, (bounds.Width - safe * 2 - gap) * .37);
                    DrawCover(dc, artwork, new Rect(bounds.Width - safe - side, bounds.Height * .48 - side / 2, side, side), 12, CoverFrame.Card);
                }
                break;
            case LyricsLayout.Cinema:
                DrawCinema(dc, bounds);
                break;
            case LyricsLayout.Poster:
                DrawPoster(dc, bounds);
                break;
            case LyricsLayout.Slices:
                DrawSlices(dc, bounds, artwork);
                break;
            case LyricsLayout.Editorial:
                if (bounds.Width >= 780)
                {
                    var paper = Math.Clamp(bounds.Width * .21, 250, 312);
                    DrawCover(dc, artwork, new Rect(Math.Clamp(bounds.Width * .031, 40, 56), bounds.Height * .25, paper, paper), 12, CoverFrame.Paper);
                }
                break;
            case LyricsLayout.CoverOnly:
                var frame = LyricsStyleMetrics.CoverOnlyFrame(bounds.Width, bounds.Height);
                if (artwork is null)
                {
                    var side = Math.Min(180, frame.Width * .42);
                    dc.DrawEllipse(placeholder, null, new Point(bounds.Width / 2, frame.Center.Y), side / 2, side / 2);
                }
                else DrawCover(dc, artwork, frame, 10, CoverFrame.Sleeve);
                break;
        }
    }

    private void DrawDrift(DrawingContext dc, Rect bounds, double phase)
    {
        var sheet = bounds.Inflate(new Thickness(bounds.Width * .28, bounds.Height * .28));
        if (atmosphere.DriftPrimary is { } primary)
            using (dc.PushOpacity(.46))
            using (dc.PushTransform(LyricsStageAtmosphere.Drift(sheet, phase, 18000, -.07, -.03, -2.4, .06, .03, 1.8, -.04, -.02, -1.5)))
            {
                dc.DrawRectangle(primary, null, sheet);
                if (atmosphere.DriftGlowA is { } glow) dc.DrawRectangle(glow, null, sheet);
                if (atmosphere.DriftGlowB is { } other) dc.DrawRectangle(other, null, sheet);
            }
        if (atmosphere.DriftCounter is { } counter)
            using (dc.PushOpacity(.4))
            using (dc.PushTransform(LyricsStageAtmosphere.Drift(sheet, phase, 26000, .07, .04, 2.6, -.05, -.03, -1.6, .05, .03, 2)))
                dc.DrawRectangle(counter, null, sheet);
    }

    private static readonly LinearGradientBrush CoverMask = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        [
            new(Colors.White, 0), new(Colors.White, .42), new(Color.FromArgb(219, 255, 255, 255), .54),
            new(Color.FromArgb(133, 255, 255, 255), .68), new(Color.FromArgb(51, 255, 255, 255), .82),
            new(Color.FromArgb(15, 255, 255, 255), .92), new(Colors.Transparent, 1)
        ]
    };
    private static readonly LinearGradientBrush FieldMask = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        [
            new(Colors.White, 0), new(Colors.White, .34), new(Color.FromArgb(245, 255, 255, 255), .46),
            new(Color.FromArgb(230, 255, 255, 255), .6), new(Color.FromArgb(209, 255, 255, 255), .76),
            new(Color.FromArgb(189, 255, 255, 255), 1)
        ]
    };
    private void DrawCoverStage(DrawingContext dc, Rect bounds, Bitmap? artwork)
    {
        if (artwork is null || bounds.Width < 780) return;
        wash.Prepare(artwork, bounds.Width, bounds.Height, atmosphere.Blur, atmosphere.Brightness, atmosphere.Saturate, atmosphere.Contrast);
        if (wash.Image is { } field)
            using (dc.PushOpacity(atmosphere.WashOpacity))
            using (dc.PushOpacityMask(FieldMask, bounds))
                dc.DrawImage(field, new Rect(field.Size), bounds);
        var cover = new Rect(0, 0, bounds.Width * .72, bounds.Height);
        using (dc.PushOpacityMask(CoverMask, cover)) Image(dc, artwork, cover);
        foreach (var veil in atmosphere.Veils) dc.DrawRectangle(veil, null, bounds);
    }

    private void DrawCinema(DrawingContext dc, Rect bounds)
    {
        var bar = Math.Clamp(bounds.Height * .025, 15, 28);
        dc.DrawRectangle(letterbox, null, new Rect(0, 0, bounds.Width, bar));
        dc.DrawRectangle(letterbox, null, new Rect(0, bounds.Height - bar, bounds.Width, bar));
        EnsureChrome(bounds.Width, bounds.Height);
        if (cinemaLabel is null) return;
        var right = Math.Clamp(bounds.Width * .03, 26, 52);
        var top = Math.Clamp(bounds.Height * .044, 28, 48);
        cinemaLabel.Draw(dc, new Point(bounds.Width - right - cinemaLabel.Width, top));
    }

    private void DrawPoster(DrawingContext dc, Rect bounds)
    {
        var arm = 18d;
        foreach (var point in new[] { new Point(24, 24), new Point(bounds.Width - 24, 24), new Point(24, bounds.Height - 24), new Point(bounds.Width - 24, bounds.Height - 24) })
        {
            var left = point.X > bounds.Width / 2;
            var down = point.Y > bounds.Height / 2;
            dc.DrawLine(cornerPen, point, point + new Vector(left ? -arm : arm, 0));
            dc.DrawLine(cornerPen, point, point + new Vector(0, down ? -arm : arm));
        }
        EnsureChrome(bounds.Width, bounds.Height);
        if (posterLabel is null) return;
        var right = Math.Clamp(bounds.Width * .037, 42, 76);
        var top = Math.Clamp(bounds.Height * .053, 46, 64);
        posterLabel.Draw(dc, new Point(bounds.Width - right - 80, top));
    }

    private void DrawSlices(DrawingContext dc, Rect bounds, Bitmap? artwork)
    {
        if (artwork is null) return;
        EnsureSlices(bounds.Width, bounds.Height);
        sliceWash.Prepare(artwork, bounds.Width, bounds.Height, 0, .84, .88);
        var graded = sliceWash.Image;
        for (var i = 0; i < 7; i++)
        {
            using (dc.PushGeometryClip(slices[i]))
            {
                using (dc.PushOpacity(.9))
                {
                    if (graded is not null) dc.DrawImage(graded, new Rect(graded.Size), bounds);
                    else Image(dc, artwork, bounds);
                }
                if (i == 3) dc.DrawRectangle(sliceAccent, null, bounds);
                else dc.DrawRectangle(shadeBrush, null, bounds);
            }
            dc.DrawGeometry(null, sliceEdge, slices[i]);
        }
        dc.DrawLine(sliceEdge, new Point(0, bounds.Height * .72), new Point(bounds.Width, bounds.Height * .72));
    }

    private enum CoverFrame { Plain, Card, Rim, Paper, Sleeve }

    private void DrawCover(DrawingContext dc, Bitmap? image, Rect rect, double radius, CoverFrame frame)
    {
        if (rect.Width < 8 || rect.Height < 8) return;
        if (frame is CoverFrame.Card or CoverFrame.Rim)
            dc.DrawEllipse(coverShadow, null, rect.Center + new Vector(0, rect.Height * .08), rect.Width * .62, rect.Height * .2);
        else if (frame == CoverFrame.Paper)
            dc.DrawEllipse(paperShadow, null, rect.Center + new Vector(0, rect.Height * .05), rect.Width * .55, rect.Height * .16);
        else if (frame == CoverFrame.Sleeve)
            dc.DrawEllipse(sleeveShadow, null, rect.Center + new Vector(0, rect.Height * .04), rect.Width * .52, rect.Height * .16);
        if (image is null)
        {
            dc.DrawRectangle(placeholder, null, new RoundedRect(rect, radius));
            return;
        }
        using (dc.PushClip(new RoundedRect(rect, radius))) Image(dc, image, rect, frame == CoverFrame.Sleeve);
        if (frame == CoverFrame.Rim) dc.DrawRectangle(null, coverRim, new RoundedRect(rect, radius));
        else if (frame == CoverFrame.Paper) dc.DrawRectangle(null, paperEdge, new RoundedRect(rect, radius));
    }

    private void EnsureChrome(double width, double height)
    {
        if (cinemaLabel is not null && Math.Abs(labelWidth - width) < 1 && Math.Abs(labelHeight - height) < 1) return;
        labelWidth = width;
        labelHeight = height;
        cinemaLabel?.Dispose();
        posterLabel?.Dispose();
        var face = new Typeface("Segoe UI, Microsoft YaHei, Noto Sans CJK SC", weight: (FontWeight)680);
        cinemaLabel = new TextLayout("ECHO / CINEMA", face, 10, cinemaInk, letterSpacing: 2.2);
        posterLabel = new TextLayout("ECHO", face, 11, posterInk, textAlignment: TextAlignment.Right, maxWidth: 80, letterSpacing: .5);
    }

    private void EnsureSlices(double width, double height)
    {
        if (slices[0] is not null && Math.Abs(sliceWidth - width) < .5 && Math.Abs(sliceHeight - height) < .5) return;
        sliceWidth = width;
        sliceHeight = height;
        for (var i = 0; i < 7; i++) slices[i] = Geometry(SlicePoints(width, height, i));
    }

    private static Point[] SlicePoints(double width, double height, int index) =>
    [
        new(width * SliceTop[index], 0), new(width * SliceTop[index + 1], 0),
        new(width * SliceBottom[index + 1], height * .72), new(width * SliceBottom[index], height * .72)
    ];

    private static Rect Inflate(Rect rect, double scale) => rect.Inflate(new Thickness(rect.Width * scale, rect.Height * scale));
    internal static void Image(DrawingContext dc, Bitmap image, Rect target, bool contain = false)
    {
        var size = image.Size;
        if (contain)
        {
            var fit = Math.Min(target.Width / size.Width, target.Height / size.Height);
            var fitted = new Rect(target.X + (target.Width - size.Width * fit) / 2, target.Y + (target.Height - size.Height * fit) / 2, size.Width * fit, size.Height * fit);
            dc.DrawImage(image, new Rect(size), fitted);
            return;
        }
        var scale = Math.Max(target.Width / size.Width, target.Height / size.Height);
        var source = new Rect((size.Width - target.Width / scale) / 2, (size.Height - target.Height / scale) / 2, target.Width / scale, target.Height / scale);
        dc.DrawImage(image, source, target);
    }
    internal static void Label(DrawingContext dc, string text, double x, double y, double size, IBrush brush)
        => dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei, Noto Sans CJK SC"), size, brush), new Point(x, y));
    private static IBrush Brush(Color color) => new SolidColorBrush(color);
    internal static StreamGeometry Geometry(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(points[0], true);
        for (var i = 1; i < points.Count; i++) path.LineTo(points[i]);
        path.EndFigure(true);
        return geometry;
    }

    public void Dispose()
    {
        wash.Dispose();
        sliceWash.Dispose();
        cinemaLabel?.Dispose();
        posterLabel?.Dispose();
        cinemaLabel = posterLabel = null;
    }
}
