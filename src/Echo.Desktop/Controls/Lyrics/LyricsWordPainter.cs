using Avalonia;
using Avalonia.Media;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// The same four-stop eased feather as lyrics.css; reuse one brush rather than allocate per word/frame.
internal sealed class LyricsWordPainter
{
    private readonly BlurEffect entranceBlur = new();
    private readonly LyricsWordStylePainter styles = new();
    private readonly LinearGradientBrush feather = new()
    {
        StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(1, 0, RelativeUnit.Relative),
        GradientStops = [new(Colors.White, 0), new(Color.FromArgb(224, 255, 255, 255), .25),
            new(Color.FromArgb(158, 255, 255, 255), .5), new(Color.FromArgb(61, 255, 255, 255), .75), new(Colors.Transparent, 1)]
    };

    public void Paint(DrawingContext dc, LyricsTextLayoutCache.Entry layout, Point point, bool highlight, LyricsLayout style, double position, double entryAge = double.PositiveInfinity, string highlightStyle = "sweep", bool spatialMotion = true)
    {
        highlightStyle = LyricsHighlightCatalog.Resolve(highlightStyle, style);
        var upcoming = highlight && layout.Words.Count > 0 ? style switch
        {
            LyricsLayout.Light => .46, LyricsLayout.WordFocus => .44,
            LyricsLayout.CoverRight => .56, LyricsLayout.CoverLeft => .55, _ => .46
        } : 1;
        if (highlight && layout.Words.Count > 0 && highlightStyle != "sweep")
        {
            styles.Paint(dc, layout, point, highlightStyle, position, upcoming, spatialMotion,
                style == LyricsLayout.KineticChars ? entryAge : double.PositiveInfinity);
            return;
        }
        var entering = style == LyricsLayout.KineticChars && entryAge < 1020;
        if (!entering || layout.Words.Count == 0)
        {
            var reveal = entering ? Math.Clamp(entryAge / 460, 0, 1) : 1;
            using (dc.PushOpacity(upcoming * reveal))
            using (dc.PushTransform(Matrix.CreateTranslation(0, (1 - reveal) * layout.Primary.TextLines[0].Height * .7)))
                layout.Primary.Draw(dc, point);
        }
        if (layout.Words.Count == 0)
        {
            if (highlight)
                using (dc.PushOpacity(entering ? Math.Clamp(entryAge / 460, 0, 1) : 1)) layout.Highlight.Draw(dc, point);
            return;
        }
        var ordinal = -1; Echo.Contracts.Lyrics.LyricWord? previous = null;
        foreach (var word in layout.Words)
        {
            if (!ReferenceEquals(previous, word.Word)) { previous = word.Word; ordinal++; }
            var reveal = entering ? Math.Clamp((entryAge - Math.Min(ordinal, 8) * 70) / 460, 0, 1) : 1;
            var rise = 1 - Math.Pow(1 - reveal, 3);
            using DrawingContext.PushedState? entranceAlpha = entering ? dc.PushOpacity(reveal) : null;
            entranceBlur.Radius = 6 * (1 - rise);
            using DrawingContext.PushedState? entranceFilter = entering && reveal < 1
                ? dc.PushEffect(entranceBlur, word.Bounds.Translate(new Vector(point.X, point.Y + (1 - rise) * layout.Primary.TextLines[0].Height * .7)).Inflate(8)) : null;
            using DrawingContext.PushedState? entranceMove = entering
                ? dc.PushTransform(Matrix.CreateTranslation(0, (1 - rise) * layout.Primary.TextLines[0].Height * .7)) : null;
            if (entering)
                using (dc.PushClip(word.Bounds.Translate(new Vector(point.X, point.Y)).Inflate(1)))
                using (dc.PushOpacity(upcoming)) layout.Primary.Draw(dc, point);
            if (!highlight) continue;
            var progress = Math.Clamp((position - word.Word.StartMs) / Math.Max(1, word.Word.EndMs - word.Word.StartMs), 0, 1);
            if (progress <= 0) continue;
            var rect = word.Bounds.Translate(new Vector(point.X, point.Y));
            var edge = progress * word.TotalWidth - word.PrefixWidth;
            var featherWidth = Math.Min(Math.Min(layout.Primary.TextLines[0].Height * .34, 16),
                Math.Min(progress * word.TotalWidth, (1 - progress) * word.TotalWidth));
            var rtl = word.Direction == FlowDirection.RightToLeft;
            var solid = Math.Clamp(edge - featherWidth, 0, rect.Width);
            if (solid > 0)
                using (dc.PushClip(new Rect(rtl ? rect.Right - solid : rect.X, rect.Y, solid, rect.Height)))
                    layout.Highlight.Draw(dc, point);
            if (featherWidth <= 0 || edge + featherWidth <= 0 || edge - featherWidth >= rect.Width) continue;
            var x = rtl ? rect.Right - edge - featherWidth : rect.X + edge - featherWidth;
            feather.StartPoint = new(rtl ? 1 : 0, 0, RelativeUnit.Relative);
            feather.EndPoint = new(rtl ? 0 : 1, 0, RelativeUnit.Relative);
            using (dc.PushClip(rect))
            using (dc.PushOpacityMask(feather, new Rect(x, rect.Y, featherWidth * 2, rect.Height)))
            using (dc.PushClip(new Rect(x, rect.Y, featherWidth * 2, rect.Height)))
                layout.Highlight.Draw(dc, point);
        }
    }
}
