using Avalonia;
using Avalonia.Media;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class EqCurveControl
{
    // Bounded to four channel paths and eight band curves. Hover reuses all geometry.
    private bool curveDirty = true;
    private Rect curvePlot;
    private StreamGeometry curveLine = new(), curveArea = new();
    private StreamGeometry? otherChannel;
    private StreamGeometry? crossLeft, crossRight;
    private readonly List<(StreamGeometry Geometry, int Index, bool Selected)> bandCurves = new(8);

    private void DrawResponse(DrawingContext context, DspViewModel vm, IBrush accent)
    {
        if (curveDirty || curvePlot != Plot) PrepareCurves(vm);
        if (curveArea is { } area)
            using (context.PushOpacity(.07)) context.DrawGeometry(accent, null, area);
        foreach (var (geometry, index, selected) in bandCurves)
        {
            using (context.PushOpacity(selected ? .7 : .38))
                context.DrawGeometry(null, new Pen(DspBandPalette.Brush(index), selected ? 1.6 : 1.15) { LineCap = PenLineCap.Round }, geometry);
        }
        if (curveLine is { } line)
            context.DrawGeometry(null, new Pen(accent, 2.5) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, line);
        if (otherChannel is not null)
        {
            using (context.PushOpacity(.75))
                context.DrawGeometry(null, new Pen(accent, 1.8) { DashStyle = new DashStyle([5, 4], 0) }, otherChannel);
        }
        if (crossLeft is not null && crossRight is not null)
        {
            using (context.PushOpacity(.45))
            {
                var pen = new Pen(accent, 1.2) { DashStyle = new DashStyle([1, 4], 0) };
                context.DrawGeometry(null, pen, crossLeft); context.DrawGeometry(null, pen, crossRight);
            }
        }
    }

    private void PrepareCurves(DspViewModel vm)
    {
        var response = vm.EqModel;
        curveLine = new(); curveArea = new();
        otherChannel = response.HasScopedBands ? new() : null;
        crossLeft = response.HasCrossRoutes ? new() : null;
        crossRight = response.HasCrossRoutes ? new() : null;
        var count = Math.Clamp((int)Plot.Width, 160, 420);
        using (var path = curveLine.Open())
        using (var fill = curveArea.Open())
        using (var other = otherChannel?.Open())
        using (var leftCross = crossLeft?.Open())
        using (var rightCross = crossRight?.Open())
        {
            fill.BeginFigure(PointAt(MinimumFrequency, 0), true);
            for (var i = 0; i <= count; i++)
            {
                var frequency = FrequencyAt(i / (double)count);
                var matrix = response.At(frequency);
                if (response.UsesMidSide) matrix = matrix.InMidSide();
                var point = PointAt(frequency, vm.PreampDb + EqResponseModel.GainDb(matrix.A));
                if (i == 0) path.BeginFigure(point, false); else path.LineTo(point);
                fill.LineTo(point);
                void Add(StreamGeometryContext? target, System.Numerics.Complex value)
                {
                    if (target is null) return;
                    var p = PointAt(frequency, vm.PreampDb + EqResponseModel.GainDb(value));
                    if (i == 0) target.BeginFigure(p, false); else target.LineTo(p);
                }
                Add(other, matrix.D); Add(leftCross, matrix.B); Add(rightCross, matrix.C);
            }
            path.EndFigure(false); fill.LineTo(PointAt(20000, 0)); fill.EndFigure(true);
            other?.EndFigure(false); leftCross?.EndFigure(false); rightCross?.EndFigure(false);
        }
        bandCurves.Clear();
        var curves = vm.Bands.Where(b => b.IsShaped && b != vm.SelectedBand).OrderByDescending(b => Math.Abs(b.GainDb)).Take(7).ToList();
        if (vm.SelectedBand is { IsShaped: true } selected) curves.Add(selected);
        foreach (var band in curves)
        {
            var filter = new StreamGeometry();
            var stage = EqResponse.Prepare(band, vm.SampleRate, vm.StrengthPercent, vm.TiltDb);
            using (var path = filter.Open())
            {
                for (var i = 0; i <= 128; i++)
                {
                    var hz = FrequencyAt(i / 128d);
                    var z = System.Numerics.Complex.FromPolarCoordinates(1, -2 * Math.PI * hz / vm.SampleRate);
                    var db = 20 * Math.Log10(Math.Max(1e-9, stage.At(z, z * z).Magnitude));
                    var point = PointAt(hz, db);
                    if (i == 0) path.BeginFigure(point, false); else path.LineTo(point);
                }
                path.EndFigure(false);
            }
            bandCurves.Add((filter, band.Index, band == vm.SelectedBand));
        }
        curvePlot = Plot; curveDirty = false;
    }
}
