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
    private IBrush? ink, accent;
    public void Draw(DrawingContext dc, LyricLine value, double position, Rect area, double size, IBrush body, IBrush highlight)
    {
        var index = 0;
        while (index + 1 < value.Words.Count && value.Words[index + 1].StartMs <= position) index++;
        var centerWidth = Math.Clamp(area.Width * 0.26, 240, 400);
        var sideWidth = Math.Max(60, (area.Width - centerWidth) / 2 - 36);
        if (line != value || word != index || width != area.Width || fontSize != size || ink != body || accent != highlight)
        {
            Dispose(); line = value; word = index; width = area.Width; fontSize = size; ink = body; accent = highlight;
            var font = new Typeface("Bahnschrift, Microsoft YaHei, Noto Sans CJK SC", weight: FontWeight.SemiBold);
            var text = value.Words[index].Text;
            var focusSize = Math.Clamp(area.Width * 0.095, 84, 156) * size / 36;
            if (text.Length > 8) focusSize *= 0.5;
            else if (text.Length > 4) focusSize *= 0.7;
            current = Shape(text, font, focusSize, highlight, centerWidth, TextAlignment.Center);
            passed = Shape(string.Concat(value.Words.Take(index).Select(w => w.Text)), font, Math.Clamp(area.Width * 0.05, 36, 72) * size / 36, body, sideWidth, TextAlignment.Right);
            future = Shape(string.Concat(value.Words.Skip(index + 1).Select(w => w.Text)), font, Math.Clamp(area.Width * 0.05, 36, 72) * size / 36, body, sideWidth, TextAlignment.Left);
            label = Shape("NOW", font, 10, highlight, centerWidth, TextAlignment.Center);
        }
        var centerX = area.X + (area.Width - centerWidth) / 2;
        var mid = area.Y + area.Height / 2;
        using (dc.PushOpacity(0.88)) passed!.Draw(dc, new(area.X, mid - passed.Height / 2 - 24));
        current!.Draw(dc, new(centerX, mid - current.Height / 2));
        using (dc.PushOpacity(0.65)) future!.Draw(dc, new(centerX + centerWidth + 36, mid - future.Height / 2 + 22));
        label!.Draw(dc, new(centerX, area.Y + 12));
        dc.DrawLine(new Pen(highlight, 2), new(centerX + centerWidth * 0.27, area.Bottom - 12), new(centerX + centerWidth * 0.73, area.Bottom - 12));
    }
    private static TextLayout Shape(string text, Typeface typeface, double size, IBrush brush, double width, TextAlignment alignment)
        => new(text, typeface, size, brush, textAlignment: alignment, textWrapping: TextWrapping.Wrap, maxWidth: width,
            maxLines: 3, textTrimming: TextTrimming.CharacterEllipsis);
    public void Dispose()
    { passed?.Dispose(); current?.Dispose(); future?.Dispose(); label?.Dispose(); passed = current = future = label = null; line = null; }
}
