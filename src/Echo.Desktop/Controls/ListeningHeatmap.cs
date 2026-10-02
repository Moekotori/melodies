using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Echo.Contracts.Library;

namespace Echo.Desktop.Controls;

// Bounded 12-week grid. It schedules no animation or periodic work.
public sealed class ListeningHeatmap : Control
{
    public static readonly StyledProperty<IBrush?> CellBrushProperty = AvaloniaProperty.Register<ListeningHeatmap, IBrush?>(nameof(CellBrush));
    public IBrush? CellBrush { get => GetValue(CellBrushProperty); set => SetValue(CellBrushProperty, value); }
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<ListeningHeatmap, IBrush?>(nameof(AccentBrush));
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    private readonly double[] levels = new double[84];
    private readonly string[] tips = new string[84];
    public void SetDays(IReadOnlyList<ListeningDay> days, DateOnly today)
    {
        var start = today.AddDays(-(((int)today.DayOfWeek + 6) % 7) - 77);
        var byDate = days.Take(84).ToDictionary(day => day.Date, StringComparer.Ordinal);
        var max = Math.Max(1, days.Count == 0 ? 1 : days.Max(day => day.Plays));
        for (var i = 0; i < 84; i++)
        {
            var key = start.AddDays(i).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var day = byDate.GetValueOrDefault(key); levels[i] = day is null ? 0 : Math.Max(.18, (double)day.Plays / max);
            tips[i] = $"{key} · {day?.Plays ?? 0} · {TimeSpan.FromSeconds(day?.Seconds ?? 0):h\\:mm\\:ss}";
        }
        InvalidateVisual();
    }
    static ListeningHeatmap() => AffectsRender<ListeningHeatmap>(CellBrushProperty, AccentBrushProperty);
    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e); var point = e.GetPosition(this);
        var week = (int)((point.X - 20) / 17); var day = (int)(point.Y / 17);
        ToolTip.SetTip(this, point.X >= 20 && week is >= 0 and < 12 && day is >= 0 and < 7 ? tips[week * 7 + day] : null);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        for (var week = 0; week < 12; week++)
            for (var day = 0; day < 7; day++)
            {
                var rect = new Rect(20 + week * 17, day * 17, 14, 14);
                context.DrawRectangle(CellBrush ?? Brushes.LightGray, null, rect, 1, 1);
                var level = levels[week * 7 + day];
                if (level > 0) using (context.PushOpacity(level)) context.DrawRectangle(AccentBrush ?? Brushes.SteelBlue, null, rect, 1, 1);
            }
    }
}
