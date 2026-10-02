using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Controls;

// Exact four-point star geometry from the current ECHO SignalPathStateIcon.tsx.
// State changes redraw once; no idle animation, bitmap, or audio sampling.
public sealed class SignalPathStateIcon : Control
{
    private static readonly Geometry Star = Geometry.Parse("M11.25 3.3Q12 1.4 12.75 3.3C14.3 7.8 16.2 9.7 20.7 11.25Q22.6 12 20.7 12.75C16.2 14.3 14.3 16.2 12.75 20.7Q12 22.6 11.25 20.7C9.7 16.2 7.8 14.3 3.3 12.75Q1.4 12 3.3 11.25C7.8 9.7 9.7 7.8 11.25 3.3Z");
    public static readonly StyledProperty<string> StateProperty = AvaloniaProperty.Register<SignalPathStateIcon, string>(nameof(State), "disconnected");
    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<SignalPathStateIcon, IBrush?>(nameof(Brush));
    public string State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    static SignalPathStateIcon() => AffectsRender<SignalPathStateIcon>(StateProperty, BrushProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var brush = Brush ?? Brushes.Gray;
        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 24)))
        {
            if (State == "disconnected") context.DrawGeometry(null, new Pen(brush, 1.5, lineJoin: PenLineJoin.Round), Star);
            else
            {
                using (context.PushTransform(Matrix.CreateScale(0.84, 0.84) * Matrix.CreateTranslation(0.9, 2.9))) context.DrawGeometry(brush, null, Star);
                using (context.PushOpacity(0.9))
                using (context.PushTransform(Matrix.CreateScale(0.24, 0.24) * Matrix.CreateTranslation(17.12, 1.12))) context.DrawGeometry(brush, null, Star);
                using (context.PushOpacity(0.45)) context.DrawEllipse(brush, null, new Point(3.2, 20), 0.7, 0.7);
            }
        }
    }
}
