using Avalonia;
using Avalonia.Media;
using Echo.Desktop.Features.Lyrics;
using static Echo.Desktop.Controls.Lyrics.LyricsStagePainter;

namespace Echo.Desktop.Controls.Lyrics;

// Native motifs for every first-party look. No browser, remote font or unbounded particle list.
internal sealed class AfterglowScenePainter
{
    private AfterglowLook look = LyricsStyleCatalog.Looks[0];
    private IBrush accent = Brushes.White, secondary = Brushes.White, ink = Brushes.White;
    private Pen line = new(Brushes.White), fine = new(Brushes.White);
    public void Configure(AfterglowLook value)
    {
        look = value; accent = Brush(value.Accent); secondary = Brush(value.Secondary); ink = Brush(value.Ink);
        line = new(accent, 2); fine = new(secondary, 1);
    }
    public void Draw(DrawingContext dc, Rect rect, int lyric, double phase)
    {
        var w = rect.Width; var h = rect.Height; var variant = Math.Max(0, lyric) % 4;
        using var opacity = dc.PushOpacity(0.24);
        switch (look.Id)
        {
            case "mist":
                for (var i = 0; i < 5; i++)
                    Polygon(dc, [new(w * (i * 0.24 - 0.2), h), new(w * (i * 0.24 + 0.1), 0), new(w * (i * 0.24 + 0.3), 0), new(w * (i * 0.24), h)], i % 2 == 0 ? accent : secondary, null);
                break;
            case "night": case "mahou":
                for (var i = 0; i < 38; i++)
                {
                    var x = (i * 97 % 101) / 101.0 * w; var y = (i * 31 % 79) / 100.0 * h;
                    dc.DrawEllipse(i % 3 == 0 ? accent : secondary, null, new(x, y), i % 3 + 1, i % 3 + 1);
                }
                dc.DrawEllipse(null, line, new(w * 0.82, h * 0.23), 46, 46);
                if (look.Id == "mahou") for (var i = 0; i < 3; i++) dc.DrawEllipse(null, fine, new(w / 2, h / 2), w * (0.35 + i * 0.05), h * (0.3 + i * 0.06));
                break;
            case "dusk":
                dc.DrawEllipse(secondary, null, new(w * 0.78, h * 0.38), h * 0.2, h * 0.2);
                dc.DrawRectangle(accent, null, new Rect(w * 0.08, h * 0.29, w * 0.3, 12));
                dc.DrawRectangle(accent, null, new Rect(w * 0.11, h * 0.34, w * 0.24, 7));
                for (var i = 0; i < 2; i++) dc.DrawRectangle(accent, null, new Rect(w * (0.14 + i * 0.15), h * 0.34, 8, h * 0.51));
                break;
            case "paper":
                for (var i = 0; i < 7; i++) dc.DrawLine(fine, new(w * (0.07 + i * 0.022), h * 0.12), new(w * (0.12 + i * 0.012), h * 0.91));
                dc.DrawRectangle(accent, null, new Rect(w * 0.86, h * 0.76, 42, 42));
                Polygon(dc, [new(w * 0.7, h * 0.12), new(w * 0.84, h * 0.2), new(w * 0.78, h * 0.32)], null, fine);
                break;
            case "aozora":
                dc.DrawEllipse(accent, null, new(w * 0.85, h * 0.2), 42, 42);
                for (var i = 0; i < 4; i++) dc.DrawEllipse(Brushes.White, null, new(w * (0.12 + i * 0.05), h * 0.22 + i % 2 * 14), 45, 18);
                for (var i = 0; i < 4; i++) dc.DrawLine(fine, new(0, h * (0.78 + i * 0.06)), new(w, h * (0.75 + i * 0.06)));
                break;
            case "neon":
                for (var i = 0; i < 12; i++)
                {
                    var x = w * i / 11; var roof = h * (0.4 + (i * 7 % 5) * 0.065);
                    dc.DrawRectangle(null, i % 2 == 0 ? line : fine, new Rect(x, roof, w * 0.07, h - roof));
                    dc.DrawLine(fine, new(x + 10, 0), new(x - 10, h * 0.28));
                }
                break;
            case "matsuri":
                dc.DrawLine(fine, new(0, h * 0.16), new(w, h * 0.16));
                for (var i = 0; i < 8; i++)
                {
                    var x = w * (i + 0.5) / 8; dc.DrawLine(fine, new(x, h * 0.16), new(x, h * 0.22));
                    dc.DrawEllipse(i % 2 == 0 ? accent : secondary, null, new(x, h * 0.26), 17, 24);
                }
                break;
            case "kabuki":
                for (var i = 0; i < 18; i++) dc.DrawLine(fine, new(w / 2, h * 0.9), new(w * i / 17, h * 0.1));
                dc.DrawEllipse(accent, null, new(w * 0.83, h * 0.22), 48, 48);
                break;
            case "yuki":
                for (var i = 0; i < 32; i++) dc.DrawEllipse(i % 2 == 0 ? accent : Brushes.White, null,
                    new(w * (i * 43 % 101) / 101, h * ((i * 29 % 97) / 97.0)), 2 + i % 3, 2 + i % 3);
                Polygon(dc, [new(0, h), new(w * 0.27, h * 0.64), new(w * 0.53, h), new(w * 0.80, h * 0.58), new(w, h)], secondary, null);
                break;
            case "junkissa":
                dc.DrawRectangle(null, line, new Rect(w * 0.08, h * 0.16, w * 0.26, h * 0.62));
                dc.DrawLine(line, new(w * 0.21, h * 0.16), new(w * 0.21, h * 0.78));
                for (var i = 0; i < 6; i++) dc.DrawEllipse(null, fine, new(w * 0.83, h * 0.74), 18 + i * 9, 18 + i * 9);
                break;
            case "hokago":
                dc.DrawLine(line, new(0, h * 0.81), new(w, h * 0.81));
                for (var i = 0; i < 12; i++) dc.DrawLine(fine, new(w * i / 11, h * 0.68), new(w * i / 11, h));
                for (var i = 0; i < 16; i++) dc.DrawEllipse(accent, null, new(w * (i * 17 % 101) / 101, h * (i * 13 % 37) / 100), 6, 3);
                break;
            case "neko":
                foreach (var x in new[] { w * 0.14, w * 0.85 })
                {
                    dc.DrawEllipse(secondary, null, new(x, h * 0.78), 32, 27);
                    Polygon(dc, [new(x - 27, h * 0.76), new(x - 27, h * 0.70), new(x - 5, h * 0.74)], secondary, null);
                    Polygon(dc, [new(x + 27, h * 0.76), new(x + 27, h * 0.70), new(x + 5, h * 0.74)], secondary, null);
                }
                break;
            case "dotto": case "brick": case "block":
                for (var i = 0; i < 34; i++)
                {
                    var side = look.Id == "dotto" ? 14 : 30;
                    var x = w * (i * 37 % 101) / 101; var y = h * (0.78 + (i * 17 % 25) / 100.0);
                    dc.DrawRectangle(i % 2 == 0 ? accent : secondary, fine, new Rect(x, y, side, side));
                    if (look.Id == "block") Polygon(dc, [new(x, y), new(x + 15, y - 9), new(x + side + 15, y - 9), new(x + side, y)], ink, fine);
                }
                break;
            case "echoes": case "psyche":
                for (var i = 0; i < 7; i++) dc.DrawEllipse(null, i % 2 == 0 ? line : fine, new(w * 0.5, h * 0.5), w * (0.15 + i * 0.055), h * (0.12 + i * 0.055));
                break;
            case "time":
                var center = new Point(w * 0.83, h * 0.26); var radius = Math.Min(w, h) * 0.15;
                dc.DrawEllipse(null, line, center, radius, radius);
                for (var i = 0; i < 12; i++)
                { var angle = i * Math.PI / 6; dc.DrawLine(fine, center + new Vector(Math.Sin(angle) * radius * 0.85, Math.Cos(angle) * radius * 0.85), center + new Vector(Math.Sin(angle) * radius, Math.Cos(angle) * radius)); }
                dc.DrawLine(line, center, center + new Vector(radius * 0.5, -radius * 0.4));
                dc.DrawLine(line, center, center + new Vector(-radius * 0.1, -radius * 0.7));
                break;
            case "code":
                Label(dc, "> echo.play()", w * 0.06, h * 0.12, 18, accent);
                for (var i = 0; i < 8; i++)
                { Label(dc, $"{i + 1:00}", w * 0.06, h * (0.22 + i * 0.075), 12, secondary); dc.DrawLine(fine, new(w * 0.12, h * (0.25 + i * 0.075)), new(w * (0.17 + (i * 3 % 5) * 0.045), h * (0.25 + i * 0.075))); }
                break;
            case "math":
                for (var i = 0; i < 25; i++)
                { var x = w * i / 24; dc.DrawLine(fine, new(x, h * 0.74 + Math.Sin(i * 0.7 + variant) * 25), new(w * (i + 1) / 24, h * 0.74 + Math.Sin((i + 1) * 0.7 + variant) * 25)); }
                Label(dc, "∑  sin(x)  ∫", w * 0.08, h * 0.12, 24, accent);
                break;
            case "icpc":
                var points = Enumerable.Range(0, 8).Select(i => new Point(w * (0.08 + (i % 4) * 0.26), h * (i / 4 == 0 ? 0.17 : 0.84))).ToArray();
                for (var i = 0; i < points.Length; i++)
                { dc.DrawLine(fine, points[i], points[(i + 3) % points.Length]); dc.DrawEllipse(null, line, points[i], 14, 14); }
                break;
        }
    }
}
