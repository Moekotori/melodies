using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Echo.Desktop.Features.Dsp;

public sealed class EqCurveControl : Control
{
    private DspViewModel? model;
    private readonly HashSet<EqBandViewModel> observedBands = [];
    private bool dragging;
    private static readonly double[] Frequencies = [20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000];
    public EqCurveControl()
    {
        Height = 210; ClipToBounds = true;
        DataContextChanged += (_, _) => BindModel();
        DetachedFromVisualTree += (_, _) => BindModel(null);
        AttachedToVisualTree += (_, _) => BindModel();
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
        PointerCaptureLost += (_, _) => dragging = false;
    }
    private void BindModel() => BindModel(DataContext as DspViewModel);
    private void BindModel(DspViewModel? value)
    {
        if (model is not null) { model.PropertyChanged -= Changed; model.Bands.CollectionChanged -= BandsChanged; }
        foreach (var b in observedBands) b.PropertyChanged -= Changed;
        observedBands.Clear();
        model = value;
        if (model is not null) { model.PropertyChanged += Changed; model.Bands.CollectionChanged += BandsChanged; ObserveBands(); }
        InvalidateVisual();
    }
    private void BandsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        ObserveBands();
        InvalidateVisual();
    }
    private void ObserveBands()
    {
        foreach (var b in observedBands.Where(b => model?.Bands.Contains(b) != true).ToArray())
        { b.PropertyChanged -= Changed; observedBands.Remove(b); }
        if (model is not null) foreach (var b in model.Bands) if (observedBands.Add(b)) b.PropertyChanged += Changed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs args) => InvalidateVisual();
    private Rect Plot => new(48, 28, Math.Max(1, Bounds.Width - 66), Math.Max(1, Bounds.Height - 62));
    private Point PointAt(double frequency, double db) => new(Plot.X + Math.Log10(Math.Clamp(frequency, 20, 20000) / 20) / 3 * Plot.Width,
        Plot.Y + (12 - Math.Clamp(db, -24, 12)) / 36 * Plot.Height);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
        var muted = Brush("EchoMuted"); var border = Brush("EchoBorder"); var accent = Brush("EchoAccent");
        Text(context, "频率响应", new(Plot.Left, 3), muted);
        Text(context, "20 Hz — 20 kHz", new(Math.Max(Plot.Left, Plot.Right - 126), 3), muted);
        foreach (var db in new[] { 12, 6, 0, -6, -12, -18, -24 })
        {
            var y = PointAt(20, db).Y;
            using (context.PushOpacity(db == 0 ? .3 : .25)) context.DrawLine(new Pen(db == 0 ? muted : border, db == 0 ? 1 : 0.5), new(Plot.Left, y), new(Plot.Right, y));
            Text(context, $"{db:+0;-0;0}", new(4, y - 7), muted);
        }
        foreach (var hz in Frequencies)
        {
            var x = PointAt(hz, 0).X;
            using (context.PushOpacity(.25)) context.DrawLine(new Pen(border, 0.5), new(x, Plot.Top), new(x, Plot.Bottom));
            Text(context, hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}", new(x - 10, Plot.Bottom + 10), muted);
        }
        if (model is not { IsReady: true } vm) return;
        var count = Math.Clamp((int)Plot.Width, 128, 600);
        var geometry = new StreamGeometry();
        var area = new StreamGeometry();
        using (var path = geometry.Open())
        using (var fill = area.Open())
        {
            fill.BeginFigure(PointAt(20, 0), true);
            for (var i = 0; i <= count; i++)
            {
                var frequency = 20 * Math.Pow(1000, i / (double)count);
                var point = PointAt(frequency, vm.PreampDb + EqResponse.Db(vm.Bands, frequency, vm.SampleRate, vm.StrengthPercent, vm.TiltDb));
                if (i == 0) path.BeginFigure(point, false); else path.LineTo(point);
                fill.LineTo(point);
            }
            path.EndFigure(false);
            fill.LineTo(PointAt(20000, 0)); fill.EndFigure(true);
        }
        using (context.PushOpacity(.065)) context.DrawGeometry(accent, null, area);
        var contributors = vm.Bands.Where(b => b.Enabled && Math.Abs(b.GainDb) > .01).OrderByDescending(b => b == vm.SelectedBand).ThenByDescending(b => Math.Abs(b.GainDb)).Take(8);
        foreach (var band in contributors)
        {
            var filter = new StreamGeometry(); EqBandViewModel[] single = [band];
            using (var path = filter.Open())
            {
                for (var i = 0; i <= 192; i++)
                {
                    var frequency = 20 * Math.Pow(1000, i / 192d);
                    var point = PointAt(frequency, EqResponse.Db(single, frequency, vm.SampleRate, vm.StrengthPercent, 0));
                    if (i == 0) path.BeginFigure(point, false); else path.LineTo(point);
                }
                path.EndFigure(false);
            }
            using (context.PushOpacity(.5)) context.DrawGeometry(null, new Pen(DspBandPalette.Brush(band.Index), 1.4), filter);
        }
        context.DrawGeometry(null, new Pen(accent, 2.8), geometry);
        foreach (var band in vm.Bands)
        {
            var point = PointAt(band.FrequencyHz, band.GainDb);
            var selected = band == vm.SelectedBand;
            if (selected)
            {
                using (context.PushOpacity(.16)) context.DrawLine(new Pen(accent, 1), new(point.X, Plot.Top), new(point.X, Plot.Bottom));
                using (context.PushOpacity(.10)) context.DrawEllipse(accent, null, point, 11, 11);
                context.DrawEllipse(Brush("EchoField"), new Pen(accent, 1), point, 7, 7);
                context.DrawEllipse(accent, null, point, 3.5, 3.5);
                Text(context, $"{band.FrequencyLabel} Hz · {band.GainDb:+0.#;-0.#;0} dB", new(Math.Clamp(point.X + 14, Plot.Left, Math.Max(Plot.Left, Plot.Right - 130)), Math.Max(Plot.Top + 2, point.Y - 25)), accent);
            }
            else using (context.PushOpacity(band.Enabled && Math.Abs(band.GainDb) > .01 ? .9 : .25))
                context.DrawEllipse(band.Enabled && Math.Abs(band.GainDb) > .01 ? DspBandPalette.Brush(band.Index) : muted, null, point, Math.Abs(band.GainDb) > .01 ? 4 : 2, Math.Abs(band.GainDb) > .01 ? 4 : 2);
        }
    }
    private static void Text(DrawingContext context, string value, Point point, IBrush brush)
        => context.DrawText(new FormattedText(value, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, 12, brush), point);
    private void Pressed(object? sender, PointerPressedEventArgs args)
    {
        if (model is not { CanEdit: true } vm || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = args.GetPosition(this); if (!Plot.Contains(point)) return;
        var band = vm.Bands.OrderBy(b => { var p = PointAt(b.FrequencyHz, b.GainDb); return Math.Pow(p.X - point.X, 2) + Math.Pow(p.Y - point.Y, 2); }).FirstOrDefault();
        if (band is null) return; vm.SelectedBand = band; dragging = true; args.Pointer.Capture(this); args.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs args)
    {
        if (!dragging || model is not { CanEdit: true, SelectedBand: { } band }) return;
        var point = args.GetPosition(this);
        band.FrequencyHz = Math.Round(Math.Clamp(20 * Math.Pow(1000, (point.X - Plot.X) / Plot.Width), 20, 20000), 1);
        if (band.IsGainEditable) band.GainDb = Math.Round(Math.Clamp(12 - (point.Y - Plot.Y) / Plot.Height * 36, -24, 12), 1);
    }
    private void Released(object? sender, PointerReleasedEventArgs args)
    { if (dragging) { dragging = false; args.Pointer.Capture(null); args.Handled = true; } }
}
