using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Echo.Contracts.Media;

namespace Echo.Desktop.Controls;

public sealed class MeasuredWaveform : Control
{
    public static readonly StyledProperty<TrackEnvelope?> EnvelopeProperty = AvaloniaProperty.Register<MeasuredWaveform, TrackEnvelope?>(nameof(Envelope));
    public static readonly StyledProperty<double> PositionProperty = AvaloniaProperty.Register<MeasuredWaveform, double>(nameof(Position));
    public static readonly StyledProperty<bool> HeatmapProperty = AvaloniaProperty.Register<MeasuredWaveform, bool>(nameof(Heatmap));
    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<MeasuredWaveform, IBrush?>(nameof(Brush));
    public TrackEnvelope? Envelope { get => GetValue(EnvelopeProperty); set => SetValue(EnvelopeProperty, value); }
    public double Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public bool Heatmap { get => GetValue(HeatmapProperty); set => SetValue(HeatmapProperty, value); }
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    static MeasuredWaveform() => AffectsRender<MeasuredWaveform>(EnvelopeProperty, PositionProperty, HeatmapProperty, BrushProperty);
    public override void Render(DrawingContext context)
    {
        if (Envelope is not { Duration: > 0 } data || data.Peaks.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var count = Math.Clamp((int)(Bounds.Width / 4), 1, 192); var step = Bounds.Width / count;
        for (var i = 0; i < count; i++)
        {
            var index = Math.Min(data.Peaks.Count - 1, i * data.Peaks.Count / count); var value = Heatmap ? data.Energy[index] : data.Peaks[index];
            var played = i / (double)count <= Position / data.Duration; var height = Heatmap ? Bounds.Height : Math.Max(2, value * Bounds.Height);
            var brush = Heatmap ? new SolidColorBrush(Echo.Desktop.Theming.ThemeColorMath.FromHsv(225 - value * 225, .7, .95)) : Brush ?? Brushes.Gray;
            using (context.PushOpacity(played ? .95 : .4)) context.FillRectangle(brush, new Rect(i * step, (Bounds.Height - height) / 2, Math.Max(1, step - 1), height));
        }
    }
}
