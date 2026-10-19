using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Echo.Desktop.Controls.Lyrics;

internal sealed class KineticLyricsChrome : IDisposable
{
    private IBrush? ink;
    private TextLayout? previous, next;
    private Pen? rule, rail;
    private readonly BlurEffect glow = new() { Radius = 12 };
    public void DrawStage(DrawingContext dc, double width, double height, IBrush brush)
    {
        Configure(brush);
        dc.DrawLine(rail!, new(36, height * .12), new(36, height * .82));
    }
    public void DrawActive(DrawingContext dc, Point point, double width, double size, double textHeight, IBrush brush, double age)
    {
        Configure(brush);
        var dot = new Point(36, point.Y + size * .55);
        var entry = 1 - Math.Pow(1 - Math.Clamp(age / 420, 0, 1), 3);
        using (dc.PushOpacity(entry))
        {
            using (dc.PushOpacity(.5))
            using (dc.PushEffect(glow, new Rect(dot.X - 20, dot.Y - 20, 40, 40))) dc.DrawEllipse(brush, null, dot, 7.5, 7.5);
            dc.DrawEllipse(brush, null, dot, 4.5 * (.4 + .6 * entry), 4.5 * (.4 + .6 * entry));
        }
        using (dc.PushOpacity(.86))
            dc.DrawLine(rule!, new(point.X, point.Y + textHeight + 16), new(point.X + Math.Min(width, size * 12) * (1 - Math.Pow(1 - Math.Clamp(age / 520, 0, 1), 3)), point.Y + textHeight + 16));
    }
    public double DrawNeighbor(DrawingContext dc, Point point, double width, bool past, IBrush brush, double baseline)
    {
        Configure(brush);
        using (dc.PushOpacity(.42)) (past ? previous : next)!.Draw(dc, point + new Vector(0, Math.Max(0, baseline - previous!.TextLines[0].Baseline)));
        return Math.Clamp(width * .044, 44, 64);
    }
    private void Configure(IBrush brush)
    {
        if (ReferenceEquals(ink, brush)) return;
        Dispose(); ink = brush; rule = new Pen(brush, 2);
        var color = ((ISolidColorBrush)brush).Color;
        rail = new(new LinearGradientBrush
        {
            StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(0, 1, RelativeUnit.Relative),
            GradientStops = [new(Colors.Transparent, 0), new(Color.FromArgb((byte)(color.A * .2), color.R, color.G, color.B), .18),
                new(Color.FromArgb((byte)(color.A * .2), color.R, color.G, color.B), .82), new(Colors.Transparent, 1)]
        }, 1);
        var face = new Typeface("SF Mono, Menlo, Consolas, monospace");
        previous = new("PREV", face, 10, brush, letterSpacing: 3);
        next = new("NEXT", face, 10, brush, letterSpacing: 3);
    }
    public void Dispose() { previous?.Dispose(); next?.Dispose(); previous = next = null; ink = null; }
}
