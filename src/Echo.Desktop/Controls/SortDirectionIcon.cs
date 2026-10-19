using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Controls;

// Two cached directional arrows; no image leases, timer or per-frame geometry allocation.
public sealed class SortDirectionIcon : Control
{
    public static readonly StyledProperty<bool> DescendingProperty = AvaloniaProperty.Register<SortDirectionIcon, bool>(nameof(Descending));
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<SortDirectionIcon, bool>(nameof(IsActive));
    public static readonly StyledProperty<IBrush?> ActiveBrushProperty = AvaloniaProperty.Register<SortDirectionIcon, IBrush?>(nameof(ActiveBrush));
    public static readonly StyledProperty<IBrush?> IdleBrushProperty = AvaloniaProperty.Register<SortDirectionIcon, IBrush?>(nameof(IdleBrush));
    public bool Descending { get => GetValue(DescendingProperty); set => SetValue(DescendingProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public IBrush? ActiveBrush { get => GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }
    public IBrush? IdleBrush { get => GetValue(IdleBrushProperty); set => SetValue(IdleBrushProperty, value); }
    private static readonly Geometry Up = StreamGeometry.Parse("M6 12V2 M2 6L6 2L10 6");
    private static readonly Geometry Down = StreamGeometry.Parse("M6 2V12 M2 8L6 12L10 8");
    private Pen? pen;
    static SortDirectionIcon() => AffectsRender<SortDirectionIcon>(DescendingProperty, IsActiveProperty, ActiveBrushProperty, IdleBrushProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var scale = context.PushTransform(Matrix.CreateScale(Bounds.Width / 12, Bounds.Height / 14));
        var brush = IsActive ? ActiveBrush : IdleBrush;
        if (pen is null || !ReferenceEquals(pen.Brush, brush)) pen = new Pen(brush, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, Descending ? Down : Up);
    }
}
