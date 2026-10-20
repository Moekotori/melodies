using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Features.Dsp;

// Two bounded response samples; computed on selection changes, never on a frame timer.
public sealed class HeadphoneCurveControl : Control
{
    private DspViewModel? model;
    private double[] selected = [], original = [];
    public HeadphoneCurveControl()
    {
        ClipToBounds = true; MinHeight = 200;
        DataContextChanged += (_, _) => Bind(DataContext as DspViewModel);
        AttachedToVisualTree += (_, _) => Bind(DataContext as DspViewModel);
        DetachedFromVisualTree += (_, _) => Bind(null);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }
    private void Bind(DspViewModel? value)
    {
        if (model is not null) model.PropertyChanged -= Changed;
        model = value;
        if (model is not null) model.PropertyChanged += Changed;
        Prepare();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(DspViewModel.HeadphonePreviewPreset) or nameof(DspViewModel.SampleRate)) Prepare(); }
    private void Prepare()
    {
        selected = Sample(model?.HeadphonePreviewPreset);
        original = model?.CorrectionMode.Key == "listening" ? Sample(model.HeadphoneOriginalPreset) : [];
        InvalidateVisual();
    }
    private double[] Sample(JsonObject? preset)
    {
        if (preset?["bands"] is not JsonArray bands) return [];
        var rate = model?.SampleRate ?? 48000;
        double Number(JsonObject node, string key) => node[key]?.GetValue<double>() ?? 0;
        var stages = bands.OfType<JsonObject>().Where(x => x["enabled"]?.ToString() != "false").Take(31).Select(x =>
            EqResponse.Prepare(new EqBandViewModel(0, Number(x, "frequencyHz"), Number(x, "gainDb"), Number(x, "q"), x["filterType"]!.ToString(), true, true), rate, 100, 0)).ToArray();
        var result = new double[241];
        for (var i = 0; i < result.Length; i++)
        {
            var z = Complex.FromPolarCoordinates(1, -2 * Math.PI * 20 * Math.Pow(1000, i / 240d) / rate);
            var db = Number(preset, "preampDb");
            foreach (var stage in stages) db += 20 * Math.Log10(Math.Max(1e-9, stage.At(z, z * z).Magnitude));
            result[i] = double.IsFinite(db) ? db : -60;
        }
        return result;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
        var muted = Brush("DspPlotMuted"); var grid = Brush("DspPlotGrid"); var accent = Brush("DspPlotCurve");
        var plot = new Rect(43, 20, Math.Max(1, Bounds.Width - 60), Math.Max(1, Bounds.Height - 52));
        var low = Math.Max(-60, Math.Min(-12, Math.Floor(selected.Concat(original).DefaultIfEmpty(-12).Min() / 6) * 6));
        var high = Math.Min(36, Math.Max(6, Math.Ceiling(selected.Concat(original).DefaultIfEmpty(6).Max() / 6) * 6));
        Point At(double x, double db) => new(plot.X + x * plot.Width, plot.Y + (high - Math.Clamp(db, low, high)) / (high - low) * plot.Height);
        for (var db = low; db <= high; db += high - low > 36 ? 12 : 6)
        {
            var y = At(0, db).Y;
            context.DrawLine(new Pen(grid, db == 0 ? 1.2 : .6), new(plot.Left, y), new(plot.Right, y));
            Text($"{db:+0;-0;0}", new(5, y - 7), muted);
        }
        foreach (var hz in new[] { 20, 100, 1000, 10000, 20000 })
        {
            var x = Math.Log10(hz / 20d) / 3; var p = At(x, 0);
            context.DrawLine(new Pen(grid, .6), new(p.X, plot.Top), new(p.X, plot.Bottom));
            Text(hz >= 1000 ? $"{hz / 1000}k" : hz.ToString(), new(p.X - 8, plot.Bottom + 10), muted);
        }
        Draw(original, muted, true); Draw(selected, accent, false);
        if (selected.Length == 0) Text("选择一个校正来源", new(plot.X + 16, plot.Y + 36), muted);
        void Text(string value, Point point, IBrush brush) => context.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, brush), point);
        void Draw(double[] data, IBrush brush, bool dashed)
        {
            if (data.Length == 0) return;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            { for (var i = 0; i < data.Length; i++) { var p = At(i / 240d, data[i]); if (i == 0) path.BeginFigure(p, false); else path.LineTo(p); } path.EndFigure(false); }
            var pen = new Pen(brush, dashed ? 1.2 : 2.2);
            if (dashed) pen.DashStyle = DashStyle.Dash;
            context.DrawGeometry(null, pen, geometry);
        }
    }
}
