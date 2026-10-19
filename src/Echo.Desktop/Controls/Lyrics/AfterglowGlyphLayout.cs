using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Echo.Contracts.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// Ports afterglowGlyphTiming.ts / afterglowGlyphMotion.ts. No per-character controls or timers.
internal sealed class AfterglowGlyphLayout : IDisposable
{
    private readonly List<Glyph> glyphs = [];
    private LyricLine? line; private double width, size; private IBrush? ink;
    private LyricLine? oversized;
    public bool Draw(DrawingContext dc, LyricLine value, double position, Rect bounds, double px, IBrush brush, bool reduced)
    {
        if (value.Words.Count == 0) return false;
        if (ReferenceEquals(oversized, value)) return false;
        if (line != value || width != bounds.Width || size != px || ink != brush)
        {
            Dispose(); line = value; width = bounds.Width; size = px; ink = brush;
            var font = new Typeface(LyricsReadingPresentation.Sans, weight: FontWeight.Bold);
            foreach (var word in value.Words)
            {
                var starts = StringInfo.ParseCombiningCharacters(word.Text);
                for (var i = 0; i < starts.Length; i++)
                {
                    if (glyphs.Count == 192) { Dispose(); oversized = value; return false; }
                    var text = word.Text.Substring(starts[i], (i + 1 < starts.Length ? starts[i + 1] : word.Text.Length) - starts[i]);
                    var start = word.StartMs + (word.EndMs - word.StartMs) * i / starts.Length;
                    var end = word.StartMs + (word.EndMs - word.StartMs) * (i + 1) / starts.Length;
                    glyphs.Add(new(new TextLayout(text, font, px, brush), start, end, Geometry(glyphs.Count, start)));
                }
            }
            double x = 0, y = 0;
            foreach (var glyph in glyphs)
            {
                var advance = glyph.Text.Width + px * .12;
                if (x > 0 && x + advance > bounds.Width) { CenterRow(y, x); x = 0; y += px * 1.93; }
                glyph.Position = new(x, y); x += advance;
            }
            CenterRow(y, x);
            var height = y + px * 1.35;
            foreach (var glyph in glyphs) glyph.Position = glyph.Position + new Vector(0, (bounds.Height - height) / 2);
        }
        foreach (var glyph in glyphs)
        {
            var duration = Math.Max(0, glyph.End - glyph.Start); var instant = duration < 100;
            var lead = instant ? 0 : duration < 180 ? 80 : 150;
            var age = Math.Max(0, position - glyph.Start + lead) / 1000; var tail = Math.Max(0, position - glyph.End) / 1000;
            var entered = position >= glyph.Start - lead;
            var opacity = !entered ? reduced ? .35 : 0 : (instant ? 1 : Clamp(age / .1)) * (1 - .18 * Ease(tail / .5));
            if (opacity <= 0) continue;
            var arrival = instant ? 1 : Spring(age);
            var activeScale = glyph.Layout.Scale * 1.4;
            var releaseScale = .5 + (activeScale - .5) * (instant ? 1 : Spring((duration + lead) / 1000));
            var scale = reduced ? 1 : position <= glyph.End ? .5 + (activeScale - .5) * arrival : releaseScale + (glyph.Layout.Scale - releaseScale) * Ease(tail / .5);
            var angle = reduced ? 0 : (glyph.Layout.Rotation + 20 * (1 - arrival) + glyph.Layout.ExitRotation * Clamp(tail / 5)) * Math.PI / 180;
            var point = new Point(bounds.X + glyph.Position.X, bounds.Y + glyph.Position.Y);
            var center = point + new Vector(glyph.Text.Width / 2, glyph.Text.Height / 2);
            var dx = reduced ? 0 : glyph.Layout.X + glyph.Layout.EnterX * (1 - arrival);
            var dy = reduced ? 0 : glyph.Layout.Y + glyph.Layout.EnterY * (1 - arrival);
            using (dc.PushOpacity(opacity))
            using (dc.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateScale(scale, scale)
                * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(center.X + dx, center.Y + dy))) glyph.Text.Draw(dc, point);
        }
        return true;
    }
    private void CenterRow(double y, double rowWidth)
    { foreach (var glyph in glyphs) if (glyph.Position.Y == y) glyph.Position += new Vector(Math.Max(0, (width - rowWidth) / 2), 0); }
    private static Layout Geometry(int index, double start)
    {
        double Sample(uint salt)
        {
            var hash = unchecked((uint)Math.Round(start) ^ ((uint)(index + 1) * 0x45d9f3b) ^ salt);
            hash = unchecked((hash ^ (hash >> 16)) * 0x45d9f3b); return (hash ^ (hash >> 16)) / (double)uint.MaxValue;
        }
        return new(1.1 + Sample(17) * .2, (Sample(31) - .5) * 40, (Sample(47) - .5) * 40,
            (Sample(61) - .5) * 10, (Sample(79) - .5) * 45, (Sample(97) - .5) * 200, (Sample(113) - .5) * 100);
    }
    private static double Clamp(double x) => Math.Clamp(x, 0, 1);
    private static double Ease(double x) { var t = Clamp(x); return t * t * (3 - 2 * t); }
    private static double Spring(double x) => 1 - Math.Exp(-10 * x) * (Math.Cos(10 * x) + Math.Sin(10 * x));
    public void Dispose() { foreach (var glyph in glyphs) glyph.Text.Dispose(); glyphs.Clear(); line = null; oversized = null; }
    private readonly record struct Layout(double Scale, double X, double Y, double Rotation, double ExitRotation, double EnterX, double EnterY);
    private sealed class Glyph(TextLayout text, double start, double end, Layout layout)
    { public TextLayout Text { get; } = text; public double Start { get; } = start; public double End { get; } = end; public Layout Layout { get; } = layout; public Point Position { get; set; } }
}
