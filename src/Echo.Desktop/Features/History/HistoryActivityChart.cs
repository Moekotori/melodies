using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.History;

// Daily bars for <=45 days, monthly buckets for longer spans. No timers or per-bar controls.
public sealed class HistoryActivityChart : Control
{
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<HistoryActivityChart, IBrush?>(nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> CellBrushProperty = AvaloniaProperty.Register<HistoryActivityChart, IBrush?>(nameof(CellBrush));
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush? CellBrush { get => GetValue(CellBrushProperty); set => SetValue(CellBrushProperty, value); }
    private ListeningDay[] series = [];
    private int selected;
    private bool byPlays;
    private double maximum;
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
    public event Action<string>? OpenDay;
    public event Action<ListeningDay, bool>? SelectionChanged;
    static HistoryActivityChart() => AffectsRender<HistoryActivityChart>(AccentBrushProperty, CellBrushProperty);
    public HistoryActivityChart() => Focusable = true;
    public void SetDays(IReadOnlyList<ListeningDay> values)
    {
        if (values.Count == 0) { series = []; InvalidateVisual(); return; }
        var end = DateOnly.ParseExact(values[^1].Date, "yyyy-MM-dd");
        var start = DateOnly.ParseExact(values[0].Date, "yyyy-MM-dd");
        if (end.DayNumber - start.DayNumber > 365) start = end.AddDays(-365);
        var monthly = end.DayNumber - start.DayNumber > 44;
        var index = values.TakeLast(366).ToDictionary(day => day.Date);
        var buckets = new List<ListeningDay>(45);
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var key = day.ToString("yyyy-MM-dd"); var value = index.GetValueOrDefault(key);
            var bucket = monthly ? key[..7] : key;
            if (buckets.Count == 0 || buckets[^1].Date != bucket) buckets.Add(new(bucket, 0, 0));
            buckets[^1] = buckets[^1] with { Plays = buckets[^1].Plays + (value?.Plays ?? 0), Seconds = buckets[^1].Seconds + (value?.Seconds ?? 0) };
        }
        series = buckets.ToArray(); selected = series.Length - 1; SetMetric(byPlays);
    }
    public void SetMetric(bool plays)
    { byPlays = plays; maximum = Math.Max(1, series.Length == 0 ? 1 : series.Max(Value)); Publish(); InvalidateVisual(); }
    private double Value(ListeningDay day) => byPlays ? day.Plays : day.Seconds;
    private void Publish() { if (series.Length > 0) SelectionChanged?.Invoke(series[selected], byPlays); }
    private int Index(Point point)
    {
        const double axis = 42;
        var i = (int)((point.X - axis) / Math.Max(1, (Bounds.Width - axis) / Math.Max(1, series.Length)));
        return point.X >= axis && point.X < Bounds.Width && point.Y >= 0 && point.Y < Bounds.Height && i < series.Length ? i : -1;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); var i = Index(e.GetPosition(this));
        if (i < 0 || i == selected) return; selected = i; Publish(); InvalidateVisual();
        ToolTip.SetTip(this, $"{series[i].Date} · {series[i].Plays:N0} · {HistoryText.Clock(series[i].Seconds)}");
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); var i = Index(e.GetPosition(this));
        if (i >= 0 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { selected = i; Focus(); OpenDay?.Invoke(series[i].Date); }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if (series.Length == 0) return;
        if (e.Key is Key.Left or Key.Right) { selected = Math.Clamp(selected + (e.Key == Key.Left ? -1 : 1), 0, series.Length - 1); Publish(); InvalidateVisual(); e.Handled = true; }
        if (e.Key == Key.Enter) { OpenDay?.Invoke(series[selected].Date); e.Handled = true; }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); if (series.Length == 0) return;
        const double axis = 42;
        var step = Math.Max(1, Bounds.Width - axis) / series.Length;
        var height = Math.Max(1, Bounds.Height - 24);
        var ink = CellBrush ?? Brushes.Gray;
        for (var level = 0; level <= 2; ++level)
        {
            var value = maximum * level / 2; var y = height - height * level / 2;
            var label = byPlays ? (value >= 1000 ? (value/1000).ToString("0.#", Culture) + "k" : value.ToString("0", Culture))
                : value >= 3600 ? (value/3600).ToString("0.#", Culture) + "h" : (value/60).ToString("0", Culture) + "m";
            var tick = new FormattedText(label, Culture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, ink);
            context.DrawText(tick, new Point(0, Math.Max(0, y - tick.Height / 2)));
            using (context.PushOpacity(.13)) context.DrawLine(new Pen(ink, 1), new Point(axis, y), new Point(Bounds.Width, y));
        }
        for (var i = 0; i < series.Length; ++i)
        {
            var barHeight = Math.Max(Value(series[i]) > 0 ? 2 : 0, Value(series[i]) / maximum * height);
            var width = Math.Min(44, Math.Max(1, step - 5)); var left = axis + i * step + (step - width) / 2;
            var bar = new Rect(left, height - barHeight, width, barHeight);
            using (context.PushOpacity(i == selected ? .92 : .23 + .20 * Value(series[i]) / maximum))
                context.DrawRectangle(AccentBrush ?? Brushes.SteelBlue, null, bar, 2, 2);
            if (series.Length <= 13 || i % Math.Max(1, (int)Math.Ceiling(series.Length / 6d)) == 0 || i == series.Length - 1)
            {
                var label = series[i].Date.Length == 7 ? series[i].Date.Replace('-', '.') : series[i].Date[5..].Replace('-', '/');
                var text = new FormattedText(label, Culture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, ink);
                context.DrawText(text, new Point(Math.Clamp(axis + i * step + (step - text.Width) / 2, axis, Math.Max(axis, Bounds.Width - text.Width)), height + 9));
            }
        }
    }
}
