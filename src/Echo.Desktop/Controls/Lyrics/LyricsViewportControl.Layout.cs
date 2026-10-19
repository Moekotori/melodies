using System.Diagnostics;
using Avalonia.Media;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// Layout ownership: shape the visible neighborhood and measure finite line handoffs.
public sealed partial class LyricsViewportControl
{
    private void Advance(int next)
    {
        if (next == active || model is null) return;
        var previous = active;
        active = next;
        transition = Stopwatch.GetTimestamp();
        shift = 0;
        if (previous >= 0 && next >= 0 && Math.Abs(next - previous) == 1
            && motion?.UseSpatialMotion == true && browse < 0 && Bounds.Width > 0)
        {
            var style = model.Appearance.SelectedStyle.Layout;
            var area = LyricsStyleMetrics.TextArea(style, Bounds.Width, Bounds.Height);
            var oldMetrics = LyricsStyleMetrics.Line(style, model.Document.Lines[previous], model.Appearance, Bounds.Width, Bounds.Height, true);
            var newMetrics = LyricsStyleMetrics.Line(style, model.Document.Lines[next], model.Appearance, Bounds.Width, Bounds.Height, true);
            var oldHeight = Math.Max(Layout(previous, oldMetrics.Size, area.Width).Height, oldMetrics.MinimumHeight);
            var newHeight = Math.Max(Layout(next, newMetrics.Size, area.Width).Height, newMetrics.MinimumHeight);
            shift = Math.Sign(next - previous) * ((oldHeight + newHeight) / 2 + newMetrics.Gap);
            var line = model.Document.Lines[next];
            transitionMs = LyricsReadingPresentation.Duration(line.EndMs - line.StartMs);
            transition = Stopwatch.GetTimestamp();
        }
        BrowseChanged?.Invoke();
    }
    private LyricsTextLayoutCache.Entry Layout(int index, double size, double width, bool focus = true, int distance = 0)
    {
        var a = model!.Appearance; var style = a.SelectedStyle;
        var font = a.Font == "跟随样式" ? LyricsReadingPresentation.Font(style.Layout) : a.Font;
        var alignment = a.Alignment switch { "居中" => TextAlignment.Center, "右对齐" => TextAlignment.Right, "左对齐" => TextAlignment.Left, _ => style.Center ? TextAlignment.Center : TextAlignment.Left };
        var metrics = LyricsStyleMetrics.Line(style.Layout, model.Document.Lines[index], a, Bounds.Width, Bounds.Height, focus, distance);
        if (a.MaxCharacters > 0) width = Math.Min(width, Math.Max(size * 3, a.MaxCharacters * size * .72));
        return cache.Get(model.Document.Lines[index], new Typeface(font, weight: metrics.Weight),
            size, metrics.SecondarySize, width, alignment, ink, accent, metrics.Secondary && a.Translation && (focus || !a.CurrentTranslationOnly),
            metrics.Secondary && a.Romanization, metrics.LineHeight * a.LineHeightPercent / 110, metrics.LetterSpacing);
    }
    private double ReadingTop(int index, double width, double height)
    {
        var top = Math.Clamp(height * .16, 92, 166);
        for (var i = 0; i < index; i++)
        {
            var metrics = LyricsStyleMetrics.Line(LyricsLayout.Reading, model!.Document.Lines[i], model.Appearance, Bounds.Width, height, false);
            top += Math.Max(Layout(i, metrics.Size, width, false).Height + 14, metrics.MinimumHeight) + metrics.Gap;
        }
        return top;
    }
}
