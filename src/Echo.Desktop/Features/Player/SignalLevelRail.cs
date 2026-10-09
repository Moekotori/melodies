using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Echo.Desktop.Features.Player;

// One rounded rail. The fill is clipped to the current percent, so warmer colors appear only near full scale.
public sealed class SignalLevelRail : Control
{
    private ThemeVariant? fillTheme;
    private IBrush? fill;
    public static readonly StyledProperty<double> PercentProperty = AvaloniaProperty.Register<SignalLevelRail, double>(nameof(Percent));
    public double Percent { get => GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    static SignalLevelRail() => AffectsRender<SignalLevelRail>(PercentProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var track = this.TryFindResource("EchoPanelMuted", ActualThemeVariant, out var muted) ? muted as IBrush : Brushes.Gray;
        var rail = new RoundedRect(new Rect(0, 0, Bounds.Width, Bounds.Height), 3);
        context.DrawRectangle(track, null, rail);
        var width = Bounds.Width * Math.Clamp(Percent, 0, 100) / 100;
        if (width <= 0) return;
        using (context.PushClip(new Rect(0, 0, width, Bounds.Height)))
            context.DrawRectangle(Fill(), null, rail);
    }
    private IBrush Fill()
    {
        if (fill is not null && fillTheme == ActualThemeVariant) return fill;
        fillTheme = ActualThemeVariant;
        Color ColorOf(string key, string fallback) => TryGetResource(key, ActualThemeVariant, out var value) && value is ISolidColorBrush solid
            ? solid.Color : Color.Parse(fallback);
        fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new(ColorOf("EchoSignalDsp", "#467bdd"), 0),
                new(ColorOf("EchoSignalModified", "#4d856f"), 0.68),
                new(ColorOf("EchoSignalStreaming", "#aa761b"), 1)
            }
        };
        return fill;
    }
}
