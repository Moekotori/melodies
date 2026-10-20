using Avalonia;
using Avalonia.Media;

namespace Echo.Desktop.Controls.Lyrics;

// Reuses the two shaped line layouts and a fixed set of drawing resources. No word controls,
// extra text shaping, timers, or unbounded caches; wrapped fragments share the word's progress.
internal sealed class LyricsWordStylePainter
{
    private readonly BlurEffect glow = new();
    private readonly BlurEffect entranceBlur = new();
    private readonly Pen decoration = new();
    private readonly RadialGradientBrush dot = new()
    {
        GradientStops = [new(Colors.White, 0), new(Colors.Transparent, 1)]
    };
    private readonly DrawingBrush dots;
    private readonly StreamGeometry star = CreateStar();

    public LyricsWordStylePainter()
    {
        dots = new DrawingBrush
        {
            Drawing = new GeometryDrawing { Brush = dot, Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1)) },
            SourceRect = new RelativeRect(0, 0, 1, 1, RelativeUnit.Absolute), DestinationRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
            TileMode = TileMode.Tile, Stretch = Stretch.Fill
        };
    }

    public void Paint(DrawingContext dc, LyricsTextLayoutCache.Entry layout, Point point, string style,
        double position, double upcoming, bool spatialMotion, double entryAge)
    {
        var size = layout.FontSize;
        var usesFocus = style is "spot" or "wave" or "lens";
        var usesBump = style is "hop" or "swell" or "halo" or "flip" or "spark" or "scan";
        var head = usesFocus ? FocusHead(layout, position) : 0;
        var ordinal = -1; Echo.Contracts.Lyrics.LyricWord? previous = null;
        for (var i = 0; i < layout.Words.Count; i++)
        {
            var word = layout.Words[i];
            if (!ReferenceEquals(previous, word.Word)) { previous = word.Word; ordinal++; }
            var p = Math.Clamp((position - word.Word.StartMs) / Math.Max(1, word.Word.EndMs - word.Word.StartMs), 0, 1);
            var bump = usesBump ? Math.Sin(p * Math.PI) : 0;
            var distance = ordinal + .5 - head;
            var focus = usesFocus ? Math.Round(Math.Exp(-distance * distance / 3) * 25) / 25 : 0;
            var rect = word.Bounds.Translate(new Vector(point.X, point.Y));
            var reveal = Math.Clamp((entryAge - Math.Min(ordinal, 8) * 70) / 460, 0, 1);
            var rise = 1 - Math.Pow(1 - reveal, 3);
            using DrawingContext.PushedState? entranceAlpha = reveal < 1 ? dc.PushOpacity(reveal) : null;
            entranceBlur.Radius = 6 * (1 - rise);
            using DrawingContext.PushedState? entranceFilter = reveal < 1 ? dc.PushEffect(entranceBlur, rect.Inflate(size)) : null;
            using DrawingContext.PushedState? entranceMove = reveal < 1
                ? dc.PushTransform(Matrix.CreateTranslation(0, (1 - rise) * layout.Primary.TextLines[0].Height * .7)) : null;
            var shift = spatialMotion ? style switch
            {
                "lift" => (1 - p) * size * .22, "hop" => -bump * size * .16,
                "wave" => -focus * size * .22, _ => 0
            } : 0;
            var scale = spatialMotion ? style switch
            {
                "swell" => 1 + bump * .12, "lens" => 1 + focus * .1, _ => 1
            } : 1;
            // Orthographic projection of ECHO's X-axis flip; glyph baseline stays fixed.
            var scaleY = spatialMotion && style == "flip" ? Math.Cos(bump * 40 * Math.PI / 180) : scale;
            var origin = new Point(rect.Center.X, rect.Y + rect.Height * .8);
            using var transform = dc.PushTransform(Matrix.CreateTranslation(-origin.X, -origin.Y)
                * Matrix.CreateScale(scale, scaleY) * Matrix.CreateTranslation(origin.X, origin.Y + shift));
            var alpha = style == "spot" ? p * .7 + focus * .3 : p;
            var layered = style is "scan" or "dots";
            using (dc.PushClip(rect))
            {
                using (dc.PushOpacity(upcoming * (layered ? Math.Clamp((1 - p) * 50, 0, 1) : 1 - alpha)))
                    layout.Primary.Draw(dc, point);
                if (!layered)
                    using (dc.PushOpacity(alpha)) layout.Highlight.Draw(dc, point);
                else if (style == "scan") DrawScan(dc, layout, word, rect, point, p, bump);
                else DrawDots(dc, layout, rect, point, p, size);
            }
            if (style == "halo" && bump > .001)
            {
                glow.Radius = size * .32;
                using (dc.PushEffect(glow, rect.Inflate(size)))
                using (dc.PushClip(rect))
                using (dc.PushOpacity(bump * .55)) layout.Highlight.Draw(dc, point);
            }
            if (style == "underline" && p > 0)
            {
                decoration.Brush = layout.Accent; decoration.Thickness = Math.Max(2, size * .05);
                var width = Math.Clamp(p * word.TotalWidth - word.PrefixWidth, 0, rect.Width);
                var rtl = word.Direction == FlowDirection.RightToLeft;
                var x = rtl ? rect.Right - width : rect.X;
                dc.DrawLine(decoration, new(x, rect.Bottom - size * .02), new(x + width, rect.Bottom - size * .02));
            }
            if (style == "spark" && bump > .001 && (i + 1 == layout.Words.Count || !ReferenceEquals(layout.Words[i + 1].Word, word.Word)))
            {
                var radius = size * .18 * (.3 + bump * 1.1);
                var x = word.Direction == FlowDirection.RightToLeft ? rect.Left : rect.Right;
                using (dc.PushOpacity(Math.Min(1, bump * 1.6)))
                using (dc.PushTransform(Matrix.CreateScale(radius, radius) * Matrix.CreateRotation(bump * 80 * Math.PI / 180)
                    * Matrix.CreateTranslation(x, rect.Top + size * .08))) dc.DrawGeometry(layout.Accent, null, star);
            }
        }
    }

    private void DrawScan(DrawingContext dc, LyricsTextLayoutCache.Entry layout, LyricsTextLayoutCache.WordClip word,
        Rect rect, Point point, double p, double bump)
    {
        var width = Math.Clamp(p * word.TotalWidth - word.PrefixWidth, 0, rect.Width);
        if (width <= 0) return;
        var rtl = word.Direction == FlowDirection.RightToLeft;
        using (dc.PushClip(new Rect(rtl ? rect.Right - width : rect.X, rect.Y, width, rect.Height))) layout.Highlight.Draw(dc, point);
        if (bump <= .001 || p * word.TotalWidth - word.PrefixWidth > rect.Width) return;
        decoration.Brush = layout.Accent; decoration.Thickness = Math.Max(1.5, layout.FontSize * .04);
        var x = rtl ? rect.Right - width : rect.X + width;
        using (dc.PushOpacity(Math.Min(1, bump * 4))) dc.DrawLine(decoration, new(x, rect.Top), new(x, rect.Bottom));
    }

    private void DrawDots(DrawingContext dc, LyricsTextLayoutCache.Entry layout, Rect rect, Point point, double p, double size)
    {
        if (p <= 0) return;
        if (p >= 1) { layout.Highlight.Draw(dc, point); return; }
        var cell = Math.Max(2, size * .2);
        dots.DestinationRect = new RelativeRect(0, 0, cell, cell, RelativeUnit.Absolute);
        // A tile's corner is sqrt(2) radii away; expanding past it settles to solid ink.
        dot.RadiusX = new(p * .75, RelativeUnit.Relative); dot.RadiusY = dot.RadiusX;
        dot.GradientStops[0].Offset = Math.Max(0, 1 - .06 / p);
        using (dc.PushOpacityMask(dots, rect)) layout.Highlight.Draw(dc, point);
    }

    private static double FocusHead(LyricsTextLayoutCache.Entry layout, double position)
    {
        var ordinal = -1; var completed = 0; Echo.Contracts.Lyrics.LyricWord? previous = null;
        for (var i = 0; i < layout.Words.Count; i++)
        {
            var word = layout.Words[i].Word;
            if (ReferenceEquals(previous, word)) continue;
            previous = word; ordinal++;
            if (position < word.StartMs) break;
            if (position < word.EndMs) return ordinal + Math.Clamp((position - word.StartMs) / Math.Max(1, word.EndMs - word.StartMs), 0, 1);
            completed = ordinal + 1;
        }
        return completed;
    }
    private static StreamGeometry CreateStar()
    {
        var result = new StreamGeometry(); using var path = result.Open();
        path.BeginFigure(new Point(0, -1), true);
        foreach (var point in new Point[] { new(.22, -.22), new(1, 0), new(.22, .22), new(0, 1), new(-.22, .22), new(-1, 0), new(-.22, -.22) }) path.LineTo(point);
        path.EndFigure(true); return result;
    }
}
