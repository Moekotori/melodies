using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Controls;

// Bounded 12-week empty grid. It schedules no animation or periodic work.
public sealed class ListeningHeatmap : Control
{
    public static readonly StyledProperty<IBrush?> CellBrushProperty = AvaloniaProperty.Register<ListeningHeatmap, IBrush?>(nameof(CellBrush));
    public IBrush? CellBrush { get => GetValue(CellBrushProperty); set => SetValue(CellBrushProperty, value); }
    static ListeningHeatmap() => AffectsRender<ListeningHeatmap>(CellBrushProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        for (var week = 0; week < 12; week++)
            for (var day = 0; day < 7; day++)
                context.DrawRectangle(CellBrush ?? Brushes.LightGray, null, new Rect(20 + week * 17, day * 17, 14, 14), 1, 1);
    }
}
