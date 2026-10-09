using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Echo.Desktop.Features.Radio;

// Decorative needles. The playback snapshot does not publish separate left/right levels.
public sealed class RadioStereoMeters : Control
{
    public static readonly StyledProperty<bool> PlayingProperty = AvaloniaProperty.Register<RadioStereoMeters, bool>(nameof(Playing));
    public static readonly StyledProperty<IBrush?> TickBrushProperty = AvaloniaProperty.Register<RadioStereoMeters, IBrush?>(nameof(TickBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<RadioStereoMeters, IBrush?>(nameof(AccentBrush));
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private double phase;
    public bool Playing { get => GetValue(PlayingProperty); set => SetValue(PlayingProperty, value); }
    public IBrush? TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    public RadioStereoMeters() => timer.Tick += (_, _) => { phase += 0.08; InvalidateVisual(); };

    static RadioStereoMeters() => AffectsRender<RadioStereoMeters>(PlayingProperty, TickBrushProperty, AccentBrushProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayingProperty) Sync();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Sync(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { timer.Stop(); base.OnDetachedFromVisualTree(e); }

    private void Sync()
    {
        if (Playing && VisualRoot is not null) timer.Start();
        else { timer.Stop(); phase = 0; InvalidateVisual(); }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var ticks = TickBrush ?? Brushes.Gray;
        var accent = AccentBrush ?? Brushes.SlateBlue;
        var width = Math.Max(0, Bounds.Width);
        var height = Math.Max(0, Bounds.Height);
        var angle = Playing ? -0.55 + 0.85 * (0.5 + 0.5 * Math.Sin(phase)) : -0.85;
        Draw(context, new Rect(0, 0, width / 2, height), "L", angle, ticks, accent);
        Draw(context, new Rect(width / 2, 0, width / 2, height), "R", angle, ticks, accent);
    }

    private static void Draw(DrawingContext context, Rect bounds, string channel, double angle, IBrush ticks, IBrush accent)
    {
        var pivot = new Point(bounds.X + bounds.Width / 2, bounds.Bottom - 18);
        var pen = new Pen(ticks, 1);
        for (var index = 0; index <= 16; index++)
        {
            var tick = -1.05 + index * 2.1 / 16;
            var inner = index % 4 == 0 ? 26 : 32;
            context.DrawLine(pen, At(pivot, tick, inner), At(pivot, tick, 40));
        }
        context.DrawLine(new Pen(accent, 1.6), pivot, At(pivot, angle, 36));
        context.DrawEllipse(accent, null, pivot, 2.5, 2.5);
        var text = new FormattedText(channel, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, ticks);
        context.DrawText(text, new(pivot.X - text.Width / 2, pivot.Y + 4));
    }

    private static Point At(Point pivot, double angle, double radius)
        => new(pivot.X + Math.Sin(angle) * radius, pivot.Y - Math.Cos(angle) * radius);
}
