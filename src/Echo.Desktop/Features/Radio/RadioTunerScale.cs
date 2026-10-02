using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Features.Radio;

// A station identifier on a 000–999 scale; no timer, frequency scan or network work.
public sealed class RadioTunerScale : Control
{
    public static readonly StyledProperty<int> ValueProperty = AvaloniaProperty.Register<RadioTunerScale, int>(nameof(Value), 276);
    public static readonly StyledProperty<IBrush?> TickBrushProperty = AvaloniaProperty.Register<RadioTunerScale, IBrush?>(nameof(TickBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<RadioTunerScale, IBrush?>(nameof(AccentBrush));
    public int Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    static RadioTunerScale() => AffectsRender<RadioTunerScale>(ValueProperty, TickBrushProperty, AccentBrushProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Math.Max(0, Bounds.Width - 8); var ticks = TickBrush ?? Brushes.Gray;
        var pen = new Pen(ticks, 1);
        for (var index = 0; index <= 64; index++)
        {
            var x = 4 + width * index / 64d;
            context.DrawLine(pen, new(x, index % 8 == 0 ? 24 : 34), new(x, 44));
        }
        var marker = 4 + width * Math.Clamp(Value, 0, 999) / 999d;
        context.DrawLine(new Pen(AccentBrush ?? Brushes.SlateBlue, 1.5), new(marker, 4), new(marker, 47));
        var triangle = new StreamGeometry();
        using (var geometry = triangle.Open())
        { geometry.BeginFigure(new(marker - 4, 1), true); geometry.LineTo(new(marker + 4, 1)); geometry.LineTo(new(marker, 7)); geometry.EndFigure(true); }
        context.DrawGeometry(AccentBrush ?? Brushes.SlateBlue, null, triangle);
    }
}
