using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Echo.Desktop.Features.Dsp;

// Parameter previews only. Rendering never requests telemetry or processes audio.
public sealed partial class DspDiagramControl : Control
{
    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<DspDiagramControl, string>(nameof(Kind), "matrix");
    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool UseThemeSurface { get; set; }
    private DspViewModel? model;
    private bool dragging;
    private string? dragParameter;
    private Rect Plot => new(36, 22, Math.Max(1, Bounds.Width - 56), Math.Max(1, Bounds.Height - 48));
    private IBrush ink = new SolidColorBrush(Color.Parse("#EDF1F8"));
    private IBrush muted = new SolidColorBrush(Color.Parse("#A8B2C2"));
    private IBrush accent = new SolidColorBrush(Color.Parse("#92A6FC"));
    private Pen grid = new(new SolidColorBrush(Color.Parse("#35404E")), 1);
    static DspDiagramControl() => AffectsRender<DspDiagramControl>(KindProperty);
    public DspDiagramControl()
    {
        ClipToBounds = true;
        DataContextChanged += (_, _) => Bind(DataContext as DspViewModel);
        AttachedToVisualTree += (_, _) => Bind(DataContext as DspViewModel);
        DetachedFromVisualTree += (_, _) => Bind(null);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        PointerPressed += (_, e) =>
        {
            if (model is not { CanEdit: true } || Kind is not ("crossfeed" or "stereoField" or "deesserBand" or "multibandBands") || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (Kind == "multibandBands")
            {
                var point = e.GetPosition(this);
                var low = At(FrequencyPosition(Value("lowCrossoverHz")), 0).X;
                var high = At(FrequencyPosition(Value("highCrossoverHz")), 0).X;
                if (Math.Min(Math.Abs(point.X - low), Math.Abs(point.X - high)) > 12)
                {
                    model.CompressionBand = model.CompressionBands[point.X < low ? 0 : point.X < high ? 1 : 2];
                    e.Handled = true; return;
                }
                dragParameter = Math.Abs(point.X - low) < Math.Abs(point.X - high) ? "lowCrossoverHz" : "highCrossoverHz";
            }
            dragging = true; e.Pointer.Capture(this); Edit(e.GetPosition(this)); e.Handled = true;
        };
        PointerMoved += (_, e) => { if (dragging) Edit(e.GetPosition(this)); };
        PointerReleased += (_, e) => { dragging = false; e.Pointer.Capture(null); };
        PointerCaptureLost += (_, _) => dragging = false;
    }
    private void Bind(DspViewModel? value)
    {
        if (model is not null) model.PropertyChanged -= Changed;
        model = value;
        if (model is not null) model.PropertyChanged += Changed;
        InvalidateVisual();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(DspViewModel.WorkbenchRevision)) InvalidateVisual(); }
    private double Value(string key) => model?.Fields.GetValueOrDefault(key)?.Value ?? 0;
    private void Edit(Point point)
    {
        var key = Kind switch { "crossfeed" => "cutoffHz", "deesserBand" => "frequencyHz", "multibandBands" => dragParameter ?? "lowCrossoverHz", _ => "width" };
        if (model?.Fields.GetValueOrDefault(key) is not { } parameter) return;
        var fraction = Math.Clamp((point.X - Plot.Left) / Plot.Width, 0, 1);
        var value = Kind switch
        {
            "crossfeed" => Math.Round(20 * Math.Pow(1000, fraction) / 10) * 10,
            "deesserBand" => Math.Round(parameter.Minimum * Math.Pow(parameter.Maximum / parameter.Minimum, fraction) / 10) * 10,
            "multibandBands" => Math.Round(20 * Math.Pow(1000, fraction)),
            _ => Math.Abs(fraction - .5) * 100 / (27 * Math.Pow(10, Value("sideGainDb") / 20))
        };
        parameter.Value = Math.Clamp(value, parameter.Minimum, parameter.Maximum);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (model is null || Bounds.Width <= 0) return;
        // Keep the empty plot area interactive too, rather than hit-testing only thin paths.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (UseThemeSurface)
        {
            ink = ThemeBrush("EchoHeading"); muted = ThemeBrush("EchoMuted"); accent = ThemeBrush("EchoAccentText");
            var line = ThemeBrush("EchoDivider");
            if (!ReferenceEquals(grid.Brush, line)) grid = new Pen(line, 1);
        }
        switch (Kind)
        {
            case "crossfeed": DrawCrossfeed(context); break;
            case "stereoField": DrawStereo(context); break;
            case "virtualStage": DrawVirtualStage(context); break;
            case "fir": DrawFir(context); break;
            case "channel": DrawChannels(context); break;
            case "room": DrawRoom(context); break;
            case "deesserBand": DrawDeesserBand(context); break;
            case "multibandBands": DrawMultibandBands(context); break;
            case "history": DrawEmptyHistory(context); break;
            case "sdmNoise": DrawSdmNoise(context); break;
            default: DrawMatrix(context); break;
        }
    }
    private IBrush ThemeBrush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
    private void Text(DrawingContext context, string value, Point point, IBrush? brush = null, double size = 11)
        => context.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), size, brush ?? muted), point);
    private void Line(DrawingContext context, Point a, Point b, double thickness = 1, IBrush? brush = null)
        => context.DrawLine(brush is null ? grid : new Pen(brush, thickness), a, b);
    private Point At(double x, double y) => new(Plot.Left + x * Plot.Width, Plot.Top + y * Plot.Height);
    private void Grid(DrawingContext context, int x = 6, int y = 4)
    {
        for (var i = 0; i <= x; i++) Line(context, At(i / (double)x, 0), At(i / (double)x, 1));
        for (var i = 0; i <= y; i++) Line(context, At(0, i / (double)y), At(1, i / (double)y));
    }
    private void Curve(DrawingContext context, Func<double, double> response, IBrush color)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            for (var i = 0; i <= 128; i++)
            { var p = At(i / 128d, Math.Clamp(response(i / 128d), 0, 1)); if (i == 0) path.BeginFigure(p, false); else path.LineTo(p); }
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(color, 2), geometry);
    }
}
