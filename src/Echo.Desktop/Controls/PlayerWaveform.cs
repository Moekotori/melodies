using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Controls;

// Original idle ornament from PlayerProgress.tsx; not an audio measurement.
// Static until bounds or theme changes. No timer or worker.
public sealed class PlayerWaveform : Control
{
    public static readonly StyledProperty<IBrush?> BarBrushProperty = AvaloniaProperty.Register<PlayerWaveform, IBrush?>(nameof(BarBrush));
    public IBrush? BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    private readonly double[] _bars = CreateBars("idle:0", 96);
    static PlayerWaveform() => AffectsRender<PlayerWaveform>(BarBrushProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var count = Bounds.Width >= 384 ? 96 : Bounds.Width >= 256 ? 64 : 48;
        var width = Math.Max(1, (Bounds.Width - (count - 1) * 2) / count);
        using (context.PushOpacity(0.276))
            for (var index = 0; index < count; index++)
            {
                var height = Math.Round(_bars[index] * 100) / 100 * Bounds.Height;
                context.DrawRectangle(BarBrush ?? Brushes.Gray, null,
                    new Rect(index * (width + 2), (Bounds.Height - height) / 2, width, height), width / 2, width / 2);
            }
    }
    private static double[] CreateBars(string seed, int count)
    {
        uint state = 2166136261;
        foreach (var character in seed) state = unchecked((state ^ character) * 16777619);
        var bars = new double[count];
        for (var index = 0; index < count; index++)
        {
            state = unchecked((state ^ (state >> 15)) * 2246822519);
            state = unchecked((state ^ (state >> 13)) * 3266489917);
            state ^= state >> 16;
            var random = state / 4294967295d;
            var phrase = Math.Sin(index * 0.23 + state % 17) * 0.18;
            var swell = Math.Sin(index / (double)(count - 1) * Math.PI) * 0.28;
            bars[index] = Math.Clamp(0.28 + random * 0.52 + phrase + swell, 0.18, 1);
        }
        return bars;
    }
}
