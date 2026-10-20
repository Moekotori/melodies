using Avalonia;
using Avalonia.Media;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspDiagramControl
{
    private void DrawCrossfeed(DrawingContext context)
    {
        Grid(context);
        var amount = Value("amount"); var cutoff = Math.Max(1, Value("cutoffHz"));
        var bs2b = model?.Options.GetValueOrDefault("model")?.Selected.Key == "bs2b";
        var floor = bs2b ? -24 : -Math.Clamp(-20 * Math.Log10(Math.Max(.0001, 1 - amount)) + 2.5, 6, 24);
        double Response(double hz, bool side) => bs2b ? Bs2bResponse(hz, cutoff, Value("feedDb"), side)
            : side ? 20 * Math.Log10(Math.Max(.0001, 1 - amount / Math.Sqrt(1 + Math.Pow(hz / cutoff, 2)))) : 0;
        Curve(context, x => (1 - Response(20 * Math.Pow(1000, x), false)) / (1 - floor), muted);
        Curve(context, x => (1 - Response(20 * Math.Pow(1000, x), true)) / (1 - floor), accent);
        var knee = Math.Log10(cutoff / 20) / 3;
        Line(context, At(knee, 0), At(knee, 1), 1, accent);
        Text(context, "Mid / Side · 拖动调整截止频率", At(.02, .03), ink);
        Text(context, "20 Hz", new(Plot.Left, Plot.Bottom + 8));
        Text(context, "1 kHz", new(Plot.Left + Plot.Width * .54, Plot.Bottom + 8));
        Text(context, "20 kHz", new(Plot.Left + Plot.Width * .84, Plot.Bottom + 8));
    }
    private static double Bs2bResponse(double hz, double cutoff, double feed, bool side)
    {
        var lowDb = -feed * 5 / 6 - 3; var highDb = feed / 6 - 3;
        var lowGain = Math.Pow(10, lowDb / 20); var highGain = 1 - Math.Pow(10, highDb / 20);
        var highCutoff = cutoff * Math.Pow(2, (lowDb - 20 * Math.Log10(highGain)) / 12);
        var lowPole = Math.Exp(-2 * Math.PI * cutoff / 48000); var highPole = Math.Exp(-2 * Math.PI * highCutoff / 48000);
        var z = System.Numerics.Complex.FromPolarCoordinates(1, -2 * Math.PI * hz / 48000);
        var low = lowGain * (1 - lowPole) / (1 - lowPole * z);
        var high = (1 - highGain * (1 - highPole) - highPole * z) / (1 - highPole * z);
        return 20 * Math.Log10(Math.Max(.000001, (high + (side ? -low : low)).Magnitude / (1 - highGain + lowGain)));
    }
    private void DrawStereo(DrawingContext context)
    {
        var center = At(.5, .52); var radius = Math.Min(Plot.Width, Plot.Height) * .4;
        Line(context, new(center.X - radius, center.Y), new(center.X + radius, center.Y));
        Line(context, new(center.X, center.Y - radius), new(center.X, center.Y + radius));
        var mid = Math.Pow(10, Value("centerGainDb") / 20); var side = Value("width") * Math.Pow(10, Value("sideGainDb") / 20);
        var rx = Math.Clamp(side * radius * .59, 1, radius); var ry = Math.Clamp(mid * radius * .59, 1, radius);
        using (context.PushOpacity(.12)) context.DrawEllipse(accent, null, center, rx, ry);
        context.DrawEllipse(null, new Pen(accent, 2), center, rx, ry);
        Text(context, "M", new(center.X + 6, center.Y - radius)); Text(context, "S", new(center.X + radius - 8, center.Y + 6));
        var correlation = (mid * mid - side * side) / Math.Max(1e-9, mid * mid + side * side);
        Text(context, $"参数相关系数 {correlation:+0.00;-0.00;0.00} · 拖动调整宽度", At(.02, 0), ink);
    }
    private void DrawVirtualStage(DrawingContext context)
    {
        var center = At(.5, .55); var radius = Math.Min(Plot.Width * .44, Plot.Height * .42);
        var radians = Value("azimuthDegrees") * Math.PI / 180;
        var strength = Value("strength");
        for (var i = 1; i <= 3; i++) using (context.PushOpacity(.3)) context.DrawEllipse(null, grid, center, radius * i / 3, radius * i / 3);
        foreach (var sign in new[] { -1, 1 })
        {
            var p = new Point(center.X + sign * Math.Sin(radians) * radius, center.Y - Math.Cos(radians) * radius);
            Line(context, center, p, 1 + strength * 3, accent);
            context.DrawEllipse(accent, null, p, 8, 8);
            Text(context, sign < 0 ? "L" : "R", new(p.X - 4, p.Y - 25), ink);
        }
        context.DrawEllipse(ink, null, center, 9, 9); Text(context, "聆听位置", new(center.X - 23, center.Y + 16));
        Text(context, $"虚拟声源 ±{Value("azimuthDegrees"):0}°", At(.02, 0), ink);
    }
    private void DrawMatrix(DrawingContext context)
    {
        var inputs = new[] { At(.1, .25), At(.1, .8) }; var outputs = new[] { At(.9, .25), At(.9, .8) };
        string[] keys = ["leftToLeft", "rightToLeft", "leftToRight", "rightToRight"];
        for (var i = 0; i < keys.Length; i++)
        {
            var coefficient = Value(keys[i]);
            using (context.PushOpacity(Math.Abs(coefficient) < .001 ? .15 : .85))
                Line(context, inputs[i % 2], outputs[i / 2], 1 + Math.Min(3, Math.Abs(coefficient) * 2), coefficient < 0 ? new SolidColorBrush(Color.Parse("#F1A17C")) : accent);
            Text(context, $"{coefficient:+0.00;-0.00;0.00}", At(.32 + i % 2 * .19, .18 + i / 2 * .52), ink);
        }
        for (var i = 0; i < 2; i++)
        { context.DrawEllipse(ink, null, inputs[i], 7, 7); context.DrawEllipse(accent, null, outputs[i], 7, 7); Text(context, i == 0 ? "L" : "R", new(inputs[i].X - 25, inputs[i].Y - 8)); Text(context, i == 0 ? "L" : "R", new(outputs[i].X + 14, outputs[i].Y - 8)); }
        Text(context, "输入", At(.05, 0)); Text(context, "输出", At(.85, 0));
    }
}
