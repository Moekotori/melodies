using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Echo.Contracts.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// Old Kinetic Poster's passed / current / future columns. Re-shape only at a word boundary.
internal sealed class KineticPosterLayout : IDisposable
{
    private LyricLine? line;
    private int word = -1;
    private double width, fontSize;
    private TextLayout? passed, current, future, label;
    private Pen rule = new(Brushes.Transparent, 2);
    private IBrush? ink, accent;
    public void Draw(DrawingContext dc, LyricLine value, double position, Rect area, double size, IBrush body, IBrush highlight, double viewportWidth = 1440)
    {
        var index = 0;
        while (index + 1 < value.Words.Count && value.Words[index + 1].StartMs <= position) index++;
        var centerWidth = Math.Clamp(viewportWidth * .22, 290, 400);
        var sidePadding = Math.Clamp(viewportWidth * .032, 24, 64);
        var sideWidth = Math.Max(60, (area.Width - centerWidth) / 2 - sidePadding);
        if (line != value || word != index || width != area.Width || fontSize != size || ink != body || accent != highlight)
        {
            Dispose(); line = value; word = index; width = area.Width; fontSize = size; ink = body; accent = highlight;
            var font = new Typeface("Bahnschrift, Microsoft YaHei, Noto Sans CJK SC", weight: (FontWeight)640);
            var text = value.Words[index].Text;
            var wide = text.Any(c => c is >= '\u2E80' and <= '\uD7FF');
            var count = text.Trim().Length;
            var focusSize = count <= (wide ? 2 : 4) ? Math.Clamp(viewportWidth * .08, 84, 156)
                : count <= (wide ? 3 : 6) ? Math.Clamp(viewportWidth * .065, 68, 120)
                : count <= (wide ? 4 : 8) ? Math.Clamp(viewportWidth * .05, 52, 90) : Math.Clamp(viewportWidth * .04, 42, 68);
            focusSize *= size / 36;
            current = Shape(text, font, focusSize, highlight, centerWidth, TextAlignment.Center);
            var sideFont = new Typeface(font.FontFamily, weight: (FontWeight)500);
            passed = Shape(string.Concat(value.Words.Take(index).Select(w => w.Text)), sideFont, Math.Clamp(viewportWidth * .05, 44, 88) * size / 36, body, sideWidth, TextAlignment.Right);
            future = Shape(string.Concat(value.Words.Skip(index + 1).Select(w => w.Text)), sideFont, Math.Clamp(viewportWidth * .05, 44, 88) * size / 36, body, sideWidth, TextAlignment.Left);
            label = Shape("NOW", font, 10, highlight, centerWidth, TextAlignment.Center);
            rule = new Pen(highlight, 2);
        }
        var centerX = area.X + (area.Width - centerWidth) / 2;
        var mid = area.Y + area.Height / 2;
        using (dc.PushOpacity(0.88)) passed!.Draw(dc, new(area.X, mid - passed.Height / 2 - 24));
        current!.Draw(dc, new(centerX, mid - current.Height / 2));
        using (dc.PushOpacity(0.84)) future!.Draw(dc, new(centerX + centerWidth + sidePadding, mid - future.Height / 2 + 22));
        label!.Draw(dc, new(centerX, area.Y + 12));
        dc.DrawLine(rule, new(centerX + centerWidth * 0.27, area.Bottom - 12), new(centerX + centerWidth * 0.73, area.Bottom - 12));
    }
    private static TextLayout Shape(string text, Typeface typeface, double size, IBrush brush, double width, TextAlignment alignment)
        => new(text, typeface, size, brush, textAlignment: alignment, textWrapping: TextWrapping.Wrap, maxWidth: width,
            maxLines: 3, textTrimming: TextTrimming.CharacterEllipsis);
    public void Dispose()
    { passed?.Dispose(); current?.Dispose(); future?.Dispose(); label?.Dispose(); passed = current = future = label = null; line = null; }
}
