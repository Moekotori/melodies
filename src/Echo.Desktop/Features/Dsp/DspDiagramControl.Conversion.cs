using Avalonia;
using Avalonia.Media;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspDiagramControl
{
    private void DrawFir(DrawingContext context)
    {
        Grid(context);
        var filter = model?.Options.GetValueOrDefault("filter1x")?.Selected.Key ?? "echo-hb";
        var soft = filter.Contains("soft", StringComparison.Ordinal) || filter.Contains("gauss", StringComparison.Ordinal);
        var steep = filter.Contains("brickwall", StringComparison.Ordinal) || filter.Contains("extreme", StringComparison.Ordinal) || filter.Contains("xl", StringComparison.Ordinal);
        var knee = soft ? 505 : steep ? 570 : 545;
        var floor = Math.Min(640, knee + (steep ? 28 : soft ? 82 : 52));
        // Same illustrative shape as ECHO's SrcFilterPreview, not an impulse FFT or a measured response.
        var geometry = new StreamGeometry();
        Point Scale(double x, double y) => At((x - 52) / 624, (y - 28) / 160);
        using (var path = geometry.Open())
        {
            path.BeginFigure(Scale(52, 54), false);
            path.CubicBezierTo(Scale(180, 54), Scale(360, 54), Scale(knee - 42, 55));
            path.CubicBezierTo(Scale(knee - 8, 56), Scale(knee, 60), Scale(knee + 8, 78));
            path.CubicBezierTo(Scale(knee + 18, 106), Scale(floor - 22, 164), Scale(floor, 177));
            path.CubicBezierTo(Scale(floor + 20, 188), Scale(650, 188), Scale(676, 188));
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(accent, 2), geometry);
        Line(context, Scale(knee, 28), Scale(knee, 188), 1, muted);
        Text(context, "0 dB", new(2, Plot.Top + 16)); Text(context, "−∞", new(4, Plot.Bottom - 10));
        Text(context, "通带", At(.1, 1.04)); Text(context, "过渡带 / Nyquist", At(.6, 1.04));
    }
    private void DrawChannels(DrawingContext context)
    {
        var balance = Value("balance");
        Text(context, "声道增益配置", At(0, 0), ink);
        for (var i = 0; i < 2; i++)
        {
            var gain = Value(i == 0 ? "leftGainDb" : "rightGainDb"); var y = .36 + i * .38;
            Text(context, i == 0 ? "L" : "R", At(0, y), ink, 17);
            Line(context, At(.1, y + .08), At(.9, y + .08), 4, muted);
            var fraction = Math.Clamp((gain + 12) / 18, 0, 1);
            Line(context, At(.1, y + .08), At(.1 + fraction * .8, y + .08), 5, accent);
            Text(context, $"{gain:+0.0;-0.0;0.0} dB", At(.73, y - .14), ink);
        }
        Text(context, $"平衡 {balance:+0.00;-0.00;0.00} · 非实时电平", At(.3, 0));
    }
    private void DrawRoom(DrawingContext context)
    {
        var labels = new[] { "源音频", "卷积 FIR", "输出声道" };
        for (var i = 0; i < 3; i++)
        {
            var p = At(.12 + i * .38, .5);
            context.DrawEllipse(i == 1 ? accent : ink, null, p, 10, 10);
            Text(context, labels[i], new(p.X - 24, p.Y + 23), ink);
            if (i < 2) Line(context, new(p.X + 14, p.Y), new(p.X + Plot.Width * .38 - 14, p.Y), 2, muted);
        }
        Text(context, "卷积处理路由", At(0, .05), ink);
    }
}
