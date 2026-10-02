using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Controls;

// Only the latest native spectrum, at most 128 measured buckets. No autonomous drawing timer.
public sealed class LiveSpectrumView : Control
{
    public static readonly StyledProperty<IReadOnlyList<float>?> SpectrumProperty = AvaloniaProperty.Register<LiveSpectrumView, IReadOnlyList<float>?>(nameof(Spectrum));
    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<LiveSpectrumView, IBrush?>(nameof(Brush));
    public IReadOnlyList<float>? Spectrum { get => GetValue(SpectrumProperty); set => SetValue(SpectrumProperty, value); }
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    static LiveSpectrumView() => AffectsRender<LiveSpectrumView>(SpectrumProperty, BrushProperty);
    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var brush = Brush ?? Brushes.Gray;
        if (Spectrum is not { Count: > 0 } data)
        { using (context.PushOpacity(.15)) context.DrawLine(new Pen(brush, 1), new(0, Bounds.Height / 2), new(Bounds.Width, Bounds.Height / 2)); return; }
        var count = Math.Min(128, data.Count); var step = Bounds.Width / count;
        for (var i = 0; i < count; i++)
        {
            var value = Math.Clamp(data[i], 0, 1); var height = Math.Max(1, Math.Sqrt(value) * Bounds.Height);
            using (context.PushOpacity(.25 + value * .7)) context.FillRectangle(brush, new Rect(i * step, (Bounds.Height - height) / 2, Math.Max(1, step - 2), height));
        }
    }
}
