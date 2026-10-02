using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Controls;

// Original seeded waveform progress from PlayerProgress.tsx; not an audio measurement.
// At most 96 bars, regenerated only for a different song/duration/width tier. No timer or worker.
public sealed class PlayerWaveform : Control
{
    public static readonly StyledProperty<IBrush?> BarBrushProperty = AvaloniaProperty.Register<PlayerWaveform, IBrush?>(nameof(BarBrush));
    public IBrush? BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public static readonly StyledProperty<IBrush?> PlayedBrushProperty = AvaloniaProperty.Register<PlayerWaveform, IBrush?>(nameof(PlayedBrush));
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<PlayerWaveform, double>(nameof(Value));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<PlayerWaveform, double>(nameof(Maximum));
    public static readonly StyledProperty<string> SeedProperty = AvaloniaProperty.Register<PlayerWaveform, string>(nameof(Seed), "idle");
    public IBrush? PlayedBrush { get => GetValue(PlayedBrushProperty); set => SetValue(PlayedBrushProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Seed { get => GetValue(SeedProperty); set => SetValue(SeedProperty, value); }
    private double[] _bars = [];
    private string _renderSeed = "";
    private double _renderDuration = -1;
    static PlayerWaveform() => AffectsRender<PlayerWaveform>(BarBrushProperty, PlayedBrushProperty, ValueProperty, MaximumProperty, SeedProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (string.IsNullOrEmpty(Seed) || Seed == "idle" || Maximum <= 0)
        {
            using (context.PushOpacity(.18))
                context.DrawRectangle(BarBrush ?? Brushes.Gray, null, new Rect(0, (Bounds.Height - 2) / 2, Bounds.Width, 2), 1, 1);
            return;
        }
        var tier = Bounds.Width >= 384 ? 96 : Bounds.Width >= 256 ? 64 : 48;
        var count = Math.Min(tier, Math.Max(1, (int)((Bounds.Width + 2) / 3)));
        var duration = Math.Round(Maximum);
        if (_bars.Length != count || _renderSeed != Seed || _renderDuration != duration)
        { _bars = CreateBars($"{Seed}:{duration}", count); _renderSeed = Seed; _renderDuration = duration; }
        var width = Math.Max(1, (Bounds.Width - (count - 1) * 2) / count);
        var progress = double.IsFinite(Value) && double.IsFinite(Maximum) && Maximum > 0 ? Math.Clamp(Value / Maximum, 0, 1) : 0;
        for (var index = 0; index < count; index++)
        {
            var played = (index + 0.5) / count <= progress;
            var height = Math.Max(4, Math.Round(_bars[index] * 100) / 100 * Bounds.Height);
            using (context.PushOpacity(played ? 0.92 : 0.276))
                context.DrawRectangle(played ? PlayedBrush ?? BarBrush ?? Brushes.Gray : BarBrush ?? Brushes.Gray, null,
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
            var swell = Math.Sin(index / (double)Math.Max(1, count - 1) * Math.PI) * 0.28;
            bars[index] = Math.Clamp(0.28 + random * 0.52 + phrase + swell, 0.18, 1);
        }
        return bars;
    }
}
