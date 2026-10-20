using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class EqCurveControl : Control
{
    private static readonly (double Hz, string Label, bool Major)[] Ticks =
    [
        (1, "1", true), (5, "5", true), (10, "10", false), (20, "20", true), (50, "50", true), (100, "100", false),
        (200, "200", true), (500, "500", true), (1000, "1k", true), (2000, "2k", true),
        (5000, "5k", true), (10000, "10k", false), (20000, "20k", true),
    ];
    private DspViewModel? model;
    private readonly HashSet<EqBandViewModel> observedBands = [];
    private bool dragging;
    private Point? hover;
    public EqCurveControl()
    {
        MinHeight = 160; ClipToBounds = true; Focusable = true; Cursor = new Cursor(StandardCursorType.Hand);
        DataContextChanged += (_, _) => BindModel();
        DetachedFromVisualTree += (_, _) => BindModel(null);
        AttachedToVisualTree += (_, _) => BindModel();
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released; PointerWheelChanged += Wheel;
        PointerExited += (_, _) => { hover = null; InvalidateVisual(); };
        PointerCaptureLost += (_, _) => dragging = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }
    private void BindModel() => BindModel(DataContext as DspViewModel);
    private void BindModel(DspViewModel? value)
    {
        if (model is not null) { model.PropertyChanged -= Changed; model.Bands.CollectionChanged -= BandsChanged; }
        foreach (var b in observedBands) b.PropertyChanged -= Changed;
        observedBands.Clear();
        model = value;
        curveDirty = true;
        if (model is not null) { model.PropertyChanged += Changed; model.Bands.CollectionChanged += BandsChanged; ObserveBands(); }
        InvalidateVisual();
    }
    private void BandsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    { ObserveBands(); curveDirty = true; InvalidateVisual(); }
    private void ObserveBands()
    {
        foreach (var b in observedBands.Where(b => model?.Bands.Contains(b) != true).ToArray())
        { b.PropertyChanged -= Changed; observedBands.Remove(b); }
        if (model is not null) foreach (var b in model.Bands) if (observedBands.Add(b)) b.PropertyChanged += Changed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is EqBandViewModel || args.PropertyName is nameof(DspViewModel.PreampDb) or nameof(DspViewModel.StrengthPercent)
            or nameof(DspViewModel.TiltDb) or nameof(DspViewModel.SampleRate) or nameof(DspViewModel.SelectedBand) or nameof(DspViewModel.EqChannelLegend)) curveDirty = true;
        InvalidateVisual();
    }
    private Rect Plot => new(44, 24, Math.Max(1, Bounds.Width - 64), Math.Max(1, Bounds.Height - 52));
    private double MinimumFrequency => model?.IsHeadphone == true ? 1 : 20;
    private double MinimumGain => model?.IsHeadphone == true ? -60 : -24;
    private double MaximumGain => model?.IsHeadphone == true ? 30 : 12;
    private double FrequencyAt(double fraction) => MinimumFrequency * Math.Pow(20000 / MinimumFrequency, Math.Clamp(fraction, 0, 1));
    private Point PointAt(double frequency, double db) => new(Plot.X
        + Math.Log10(Math.Clamp(frequency, MinimumFrequency, 20000) / MinimumFrequency) / Math.Log10(20000 / MinimumFrequency) * Plot.Width,
        Plot.Y + (MaximumGain - Math.Clamp(db, MinimumGain, MaximumGain)) / (MaximumGain - MinimumGain) * Plot.Height);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        IBrush Brush(string key)
        {
            if (model?.IsEq == true && this.TryFindResource(key.Replace("Dsp", "Eq"), ActualThemeVariant, out var themed) && themed is IBrush custom) return custom;
            return this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
        }
        var muted = Brush("DspPlotMuted"); var border = Brush("DspPlotGrid"); var accent = Brush("DspPlotCurve"); var field = Brush("DspPlotField");
        var dense = Plot.Width >= 760;
        foreach (var db in model?.IsHeadphone == true ? new[] { 30, 15, 0, -15, -30, -45, -60 } : new[] { 12, 6, 0, -6, -12, -18, -24 })
        {
            if (!dense && db is 6 or -6 or -18) continue;
            var y = PointAt(20, db).Y;
            var pen = new Pen(db == 0 ? muted : border, db == 0 ? 1 : 0.6);
            using (context.PushOpacity(db == 0 ? .5 : .42)) context.DrawLine(pen, new(Plot.Left, y), new(Plot.Right, y));
            Text(context, $"{db:+0;-0;0}", new(4, y - 6), muted, 11);
        }
        foreach (var tick in Ticks)
        {
            if (tick.Hz < MinimumFrequency || !tick.Major && !dense) continue;
            var x = PointAt(tick.Hz, 0).X;
            using (context.PushOpacity(tick.Major ? .38 : .22)) context.DrawLine(new Pen(border, 0.6), new(x, Plot.Top), new(x, Plot.Bottom));
            if (tick.Hz >= 20000) continue;
            if (tick.Major || dense) Text(context, tick.Label, new(x - (tick.Label.Length > 2 ? 12 : 8), Plot.Bottom + 9), muted, 11);
        }
        Text(context, "20k", new(Plot.Right - 22, Plot.Bottom + 9), muted, 11);
        if (model is not { IsReady: true } vm) return;
        using (context.PushClip(new Rect(Plot.X - 14, Plot.Y - 14, Plot.Width + 28, Plot.Height + 18)))
        using (context.PushOpacity(vm.Enabled ? 1 : .82))
        {
            DrawResponse(context, vm, accent);
            foreach (var band in vm.Bands) DrawNode(context, vm, band, muted, field);
        }
        if (vm.SelectedBand is { } selected) DrawReadout(context, selected, accent, field);
        if (!dragging && hover is { } cursor && Plot.Contains(cursor)) DrawHover(context, vm, cursor, muted, field);
    }
    private void DrawNode(DrawingContext context, DspViewModel vm, EqBandViewModel band, IBrush muted, IBrush field)
    {
        var gain = band.IsGainEditable ? band.GainDb : 0;
        var point = PointAt(band.FrequencyHz, gain);
        var selected = band == vm.SelectedBand;
        var color = band.IsShaped || selected ? DspBandPalette.Brush(band.Index) : muted;
        if (selected)
        {
            var guide = new Pen(color, 1) { DashStyle = new DashStyle([2, 3], 0) };
            using (context.PushOpacity(.55)) context.DrawLine(guide, new(point.X, Plot.Top), new(point.X, Plot.Bottom));
            using (context.PushOpacity(.12)) context.DrawEllipse(color, null, point, 16, 16);
        }
        var radius = selected ? 6.2 : band.IsShaped ? 4.4 : 1.6;
        if (selected || band.IsShaped) context.DrawEllipse(field, null, point, radius + 1.5, radius + 1.5);
        using (context.PushOpacity(band.Enabled ? 1 : .35)) context.DrawEllipse(color, null, point, radius, radius);
        if (selected) context.DrawEllipse(field, null, point, 2.1, 2.1);
    }
    private void DrawReadout(DrawingContext context, EqBandViewModel band, IBrush accent, IBrush field)
    {
        var point = PointAt(band.FrequencyHz, band.IsGainEditable ? band.GainDb : 0);
        var label = $"{FormatHz(band.FrequencyHz)}   {band.GainDb:+0.0;-0.0;0.0} dB";
        var text = Label(label, accent, 12, FontWeight.Medium);
        var x = Math.Clamp(point.X + 12, Plot.Left, Math.Max(Plot.Left, Plot.Right - text.Width - 8));
        var y = Math.Clamp(point.Y - 28, Plot.Top + 2, Math.Max(Plot.Top + 2, Plot.Bottom - text.Height - 4));
        context.DrawRectangle(field, new Pen(accent, .7), new Rect(x - 8, y - 5, text.Width + 16, text.Height + 10), 7, 7);
        context.DrawText(text, new Point(x, y));
    }
    private void DrawHover(DrawingContext context, DspViewModel vm, Point cursor, IBrush muted, IBrush field)
    {
        var frequency = FrequencyAt((cursor.X - Plot.X) / Plot.Width);
        var db = vm.PreampDb + vm.EqModel.Db(frequency, vm.EqModel.UsesMidSide ? "mid" : "left");
        var point = PointAt(frequency, db);
        using (context.PushOpacity(.35)) context.DrawLine(new Pen(muted, 1), new(point.X, Plot.Top), new(point.X, Plot.Bottom));
        context.DrawEllipse(field, new Pen(muted, 1), point, 3.5, 3.5);
        var label = $"{FormatHz(frequency)}   {db:+0.0;-0.0;0.0} dB";
        if (vm.EqModel.HasScopedBands)
        {
            var other = vm.PreampDb + vm.EqModel.Db(frequency, vm.EqModel.UsesMidSide ? "side" : "right");
            label += $" / {other:+0.0;-0.0;0.0} dB";
        }
        var text = Label(label, muted, 10, FontWeight.Medium);
        var x = Math.Clamp(point.X + 10, Plot.Left, Math.Max(Plot.Left, Plot.Right - text.Width - 4));
        context.DrawText(text, new Point(x, Math.Clamp(point.Y - 18, Plot.Top, Plot.Bottom - 12)));
    }
    private static string FormatHz(double hz) => hz >= 1000 ? $"{hz / 1000:0.##} kHz" : $"{hz:0} Hz";
    private FormattedText Label(string value, IBrush brush, double size, FontWeight weight)
        => new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);
    private void Text(DrawingContext context, string value, Point point, IBrush brush, double size)
        => context.DrawText(Label(value, brush, size, FontWeight.Medium), point);
    private void Pressed(object? sender, PointerPressedEventArgs args)
    {
        if (model is not { CanEdit: true } vm || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = args.GetPosition(this); if (!Plot.Contains(point)) return;
        var band = vm.Bands.OrderBy(b => { var p = PointAt(b.FrequencyHz, b.IsGainEditable ? b.GainDb : 0); return Math.Pow(p.X - point.X, 2) + Math.Pow(p.Y - point.Y, 2); }).FirstOrDefault();
        if (band is null) return;
        Focus(); vm.SelectedBand = band;
        if (args.ClickCount > 1)
        {
            dragging = false; args.Pointer.Capture(null);
            if (band.IsGainEditable) band.GainDb = 0;
            args.Handled = true; return;
        }
        dragging = true; args.Pointer.Capture(this); args.Handled = true;
    }
    private void Wheel(object? sender, PointerWheelEventArgs args)
    {
        if (model is not { CanEdit: true, SelectedBand: { } band } || args.Delta.Y == 0) return;
        var factor = args.Delta.Y > 0 ? 1.12 : 1 / 1.12;
        band.Q = Math.Round(Math.Clamp(band.Q * factor, band.QMinimum, band.QMaximum), 3);
        args.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || model is not { CanEdit: true, SelectedBand: { } band }) return;
        var fine = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Left: band.FrequencyHz = NudgeFrequency(band.FrequencyHz, false, fine, band.FrequencyMinimum); break;
            case Key.Right: band.FrequencyHz = NudgeFrequency(band.FrequencyHz, true, fine, band.FrequencyMinimum); break;
            case Key.Up when band.IsGainEditable: band.GainDb = NudgeGain(band.GainDb, true, fine, band.GainMinimum, band.GainMaximum); break;
            case Key.Down when band.IsGainEditable: band.GainDb = NudgeGain(band.GainDb, false, fine, band.GainMinimum, band.GainMaximum); break;
            case Key.Delete or Key.Back when band.IsGainEditable: band.GainDb = 0; break;
            default: return;
        }
        e.Handled = true;
    }
    private static double NudgeFrequency(double hz, bool up, bool fine, double minimum)
    {
        var factor = fine ? 1.0293 : 1.12246;
        return Math.Round(Math.Clamp(up ? hz * factor : hz / factor, minimum, 20000), 1);
    }
    private static double NudgeGain(double db, bool up, bool fine, double minimum, double maximum)
        => Math.Round(Math.Clamp(db + (up ? 1 : -1) * (fine ? 0.1 : 0.5), minimum, maximum), 1);
    private void Moved(object? sender, PointerEventArgs args)
    {
        var point = args.GetPosition(this);
        if (dragging && model is { CanEdit: true, SelectedBand: { } band })
        {
            band.FrequencyHz = Math.Round(FrequencyAt((point.X - Plot.X) / Plot.Width), 1);
            if (band.IsGainEditable) band.GainDb = Math.Round(Math.Clamp(MaximumGain - (point.Y - Plot.Y) / Plot.Height * (MaximumGain - MinimumGain), band.GainMinimum, band.GainMaximum), 1);
            return;
        }
        var next = Plot.Contains(point) ? point : (Point?)null;
        if (next != hover) { hover = next; InvalidateVisual(); }
    }
    private void Released(object? sender, PointerReleasedEventArgs args)
    { if (dragging) { dragging = false; args.Pointer.Capture(null); args.Handled = true; } }
}
