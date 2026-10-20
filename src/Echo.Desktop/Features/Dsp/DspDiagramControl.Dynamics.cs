using Avalonia;
using Avalonia.Media;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspDiagramControl
{
    private static double FrequencyPosition(double hz) => Math.Clamp(Math.Log(Math.Max(20, hz) / 20) / Math.Log(1000), 0, 1);
    private void DrawDeesserBand(DrawingContext context)
    {
        if (model?.Fields.GetValueOrDefault("frequencyHz") is not { } field) return;
        var frequency = field.Value; var q = Math.Max(.5, Value("q"));
        var sigma = frequency / q / 2.355;
        double Position(double hz) => Math.Log(hz / field.Minimum) / Math.Log(field.Maximum / field.Minimum);
        foreach (var hz in new[] { 2000, 4000, 8000, 12000 })
        {
            var x = Position(hz);
            if (x < 0 || x > 1) continue;
            Line(context, At(x, 0), At(x, 1)); Text(context, $"{hz / 1000}k", At(Math.Clamp(x - .015, 0, .96), 1.04));
        }
        Curve(context, x => 1 - Math.Exp(-.5 * Math.Pow((field.Minimum * Math.Pow(field.Maximum / field.Minimum, x) - frequency) / sigma, 2)), accent);
        var center = Position(frequency);
        Line(context, At(center, 0), At(center, 1), 1, accent);
        Text(context, $"{frequency / 1000:0.##} kHz", At(Math.Clamp(center - .04, 0, .85), -.13), ink);
        Line(context, At(0, 1), At(1, 1));
    }
    private void DrawMultibandBands(DrawingContext context)
    {
        double[] edges = [0, FrequencyPosition(Value("lowCrossoverHz")), FrequencyPosition(Value("highCrossoverHz")), 1];
        string[] labels = ["低频", "中频", "高频"];
        for (var i = 0; i < 3; i++)
        {
            var left = At(edges[i], 0); var right = At(edges[i + 1], 1);
            using (context.PushOpacity(model?.CompressionBand.Key == i.ToString() ? .14 : .045))
                context.DrawRectangle(accent, null, new Rect(left, right));
            if (right.X - left.X > 55)
            {
                Text(context, labels[i], new((left.X + right.X) / 2 - 14, Plot.Top + Plot.Height * .28), ink, 13);
                Text(context, "— dB", new((left.X + right.X) / 2 - 14, Plot.Top + Plot.Height * .58));
            }
        }
        for (var i = 1; i < 3; i++)
        {
            Line(context, At(edges[i], 0), At(edges[i], 1), 2, accent);
            Text(context, $"{Value(i == 1 ? "lowCrossoverHz" : "highCrossoverHz"):0} Hz", At(Math.Clamp(edges[i] - .04, 0, .85), 1.04), ink);
        }
    }
    private void DrawEmptyHistory(DrawingContext context)
    {
        for (var i = 0; i <= 3; i++)
        {
            Line(context, At(0, i / 3d), At(1, i / 3d));
            Text(context, i == 0 ? "0" : $"−{i * 6}", new(4, Plot.Top + Plot.Height * i / 3 - 6));
        }
        Text(context, "等待宿主衰减数据", At(.35, .43));
        Text(context, "12 秒前", At(0, 1.06)); Text(context, "现在", At(.95, 1.06));
    }
    private void DrawSdmNoise(DrawingContext context)
    {
        // ECHO also leaves the NTF plot empty when no native coefficients are available.
        var carrier = double.TryParse(model?.Options.GetValueOrDefault("carrierRate")?.Selected.Key, out var hz) ? hz : 176400;
        var maxHz = Math.Max(20000, carrier * 8);
        double Position(double frequency) => Math.Log(frequency / 20) / Math.Log(maxHz / 20);
        using (context.PushOpacity(.06)) context.DrawRectangle(accent, null, new Rect(At(0, 0), At(Position(20000), 1)));
        foreach (var db in new[] { 10, 0, -40, -80, -120, -160 })
        {
            var y = (10 - db) / 170d; Line(context, At(0, y), At(1, y));
            Text(context, db.ToString(), new(2, Plot.Top + Plot.Height * y - 6));
        }
        Text(context, "等待调制器系数", At(.35, .43));
        Text(context, "20 Hz", At(0, 1.06)); Text(context, "20 kHz", At(Position(20000), 1.06)); Text(context, $"{maxHz / 1e6:0.##} MHz", At(.9, 1.06));
    }
}
