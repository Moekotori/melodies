using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Features.Player;

// Twenty fixed segments; drawing only, no per-segment controls or animation clocks.
public sealed class SignalLevelRail : Control
{
    public static readonly StyledProperty<double> PercentProperty = AvaloniaProperty.Register<SignalLevelRail, double>(nameof(Percent));
    public double Percent { get => GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    static SignalLevelRail() => AffectsRender<SignalLevelRail>(PercentProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var track = this.TryFindResource("EchoPanelMuted", ActualThemeVariant, out var muted) ? muted as IBrush : Brushes.Gray;
        var fill = this.TryFindResource("EchoSignalModified", ActualThemeVariant, out var color) ? color as IBrush : Brushes.Green;
        var warning = this.TryFindResource("EchoSignalStreaming", ActualThemeVariant, out var amber) ? amber as IBrush : Brushes.Orange;
        var width = Bounds.Width / 20;
        for (var i = 0; i < 20; i++)
            context.FillRectangle((i + 1) * 5 <= Percent ? i >= 18 ? warning! : fill! : track!, new Rect(i * width, 0, Math.Max(0, width - 1), Bounds.Height));
    }
}
