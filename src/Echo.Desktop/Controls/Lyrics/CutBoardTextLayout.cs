using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Echo.Contracts.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

internal sealed class CutBoardTextLayout : IDisposable
{
    private static readonly double[] Columns = [.12, .20, .125, .11, .11, .235, .10];
    private readonly TextLayout?[] panels = new TextLayout[7];
    private LyricLine? line; private int current = -1; private double width;
    public void Draw(DrawingContext dc, LyricLine value, double position, Rect bounds, IBrush ink, IBrush accent)
    {
        var index = 0; while (index + 1 < value.Words.Count && value.Words[index + 1].StartMs <= position) index++;
        if (line != value || current != index || width != bounds.Width)
        {
            Dispose(); line = value; current = index; width = bounds.Width;
            var glyphTiming = value.Words.Sum(w => Math.Max(1, w.Text.Trim().Length)) / (double)value.Words.Count <= 1.75;
            var past = glyphTiming ? Math.Min(3, Math.Max(1, (int)Math.Ceiling(index * .35))) : 1;
            var future = glyphTiming ? Math.Min(2, Math.Max(1, (int)Math.Ceiling((value.Words.Count - index - 1) * .34))) : 1;
            var nearStart = Math.Max(0, index - past); var farStart = Math.Max(0, nearStart - (glyphTiming ? 6 : 3));
            var nearEnd = Math.Min(value.Words.Count, index + 1 + future); var farEnd = Math.Min(value.Words.Count, nearEnd + (glyphTiming ? 6 : 3));
            var ranges = new[] { (0, farStart), (farStart, nearStart), (nearStart, index), (index, index + 1), (index + 1, nearEnd), (nearEnd, farEnd), (farEnd, value.Words.Count) };
            for (var i = 1; i < 6; i++)
            {
                var text = string.Concat(value.Words.Skip(ranges[i].Item1).Take(ranges[i].Item2 - ranges[i].Item1).Select(w => w.Text));
                var px = i == 3 ? Math.Clamp(bounds.Width * .081, 70, 148) : Math.Clamp(bounds.Width * .047, 38, 88);
                panels[i] = new(text, new Typeface("Noto Serif CJK SC, Songti SC, SimSun", weight: (FontWeight)(i == 3 ? 620 : 560)), px,
                    i == 3 ? accent : ink, textAlignment: TextAlignment.Center, textWrapping: TextWrapping.Wrap,
                    maxWidth: bounds.Width * Columns[i], maxLines: 4, textTrimming: TextTrimming.CharacterEllipsis, lineHeight: px * .94, letterSpacing: px * -.035);
            }
        }
        double x = bounds.X;
        for (var i = 0; i < 7; i++)
        {
            if (panels[i] is { } text)
                using (dc.PushOpacity(i == 3 ? 1 : i < 3 ? .98 : .84)) text.Draw(dc, new(x, bounds.Y + (bounds.Height - text.Height) / 2));
            x += bounds.Width * Columns[i];
        }
    }
    public void Dispose() { foreach (var panel in panels) panel?.Dispose(); Array.Clear(panels); line = null; }
}
