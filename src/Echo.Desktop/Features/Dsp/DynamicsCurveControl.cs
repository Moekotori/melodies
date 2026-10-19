using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Features.Dsp;

// Static parameter response, not a fabricated live meter.
public sealed class DynamicsCurveControl : Control
{
    public static readonly StyledProperty<double> ThresholdProperty = AvaloniaProperty.Register<DynamicsCurveControl, double>(nameof(Threshold), -24);
    public static readonly StyledProperty<double> RatioProperty = AvaloniaProperty.Register<DynamicsCurveControl, double>(nameof(Ratio), 4);
    public static readonly StyledProperty<double> MakeupProperty = AvaloniaProperty.Register<DynamicsCurveControl, double>(nameof(Makeup));
    public static readonly StyledProperty<double> KneeProperty = AvaloniaProperty.Register<DynamicsCurveControl, double>(nameof(Knee));
    public double Threshold { get => GetValue(ThresholdProperty); set => SetValue(ThresholdProperty, value); }
    public double Ratio { get => GetValue(RatioProperty); set => SetValue(RatioProperty, value); }
    public double Makeup { get => GetValue(MakeupProperty); set => SetValue(MakeupProperty, value); }
    public double Knee { get => GetValue(KneeProperty); set => SetValue(KneeProperty, value); }
    static DynamicsCurveControl() => AffectsRender<DynamicsCurveControl>(ThresholdProperty, RatioProperty, MakeupProperty, KneeProperty);
    public override void Render(DrawingContext context)
    {
        var rect = new Rect(12, 12, Math.Max(1, Bounds.Width - 24), Math.Max(1, Bounds.Height - 24));
        var grid = new Pen(new SolidColorBrush(Color.Parse("#30FFFFFF")), 1);
        for (var i = 0; i <= 4; i++)
        { var x = rect.X + rect.Width * i / 4; var y = rect.Y + rect.Height * i / 4; context.DrawLine(grid, new(x, rect.Top), new(x, rect.Bottom)); context.DrawLine(grid, new(rect.Left, y), new(rect.Right, y)); }
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            for (var i = 0; i <= 96; i++)
            {
                var input = -72 + i * .75; var delta = input - Threshold;
                var reduction = delta <= -Knee / 2 ? 0 : Knee > 0 && delta < Knee / 2 ? (1 / Math.Max(1, Ratio) - 1) * Math.Pow(delta + Knee / 2, 2) / (2 * Knee) : (1 / Math.Max(1, Ratio) - 1) * delta;
                var output = Math.Clamp(input + reduction + Makeup, -72, 0);
                var point = new Point(rect.X + rect.Width * i / 96, rect.Bottom - (output + 72) / 72 * rect.Height);
                if (i == 0) path.BeginFigure(point, false); else path.LineTo(point);
            }
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(Brushes.White, 2), geometry);
    }
}
