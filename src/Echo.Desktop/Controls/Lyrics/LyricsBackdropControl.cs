using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// Retained background layer: file/cover/style/line changes redraw it; word frames only redraw text.
public sealed class LyricsBackdropControl : Control, IDisposable
{
    private readonly LyricsStagePainter painter = new();
    private LyricsViewModel? model;
    private int line = int.MinValue;
    private bool closed;
    private long lastFrame;
    private double phase;
    public LyricsBackdropControl()
    {
        IsHitTestVisible = false; ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => Configure();
        SizeChanged += (_, _) => InvalidateVisual();
        DetachedFromVisualTree += (_, _) => Dispose();
    }
    public void Bind(LyricsViewModel value)
    {
        model = value; model.Changed += Changed; model.PropertyChanged += PropertyChangedModel;
        model.Appearance.PropertyChanged += PropertyChangedAppearance; Configure();
    }
    private void PropertyChangedModel(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(LyricsViewModel.Cover) or nameof(LyricsViewModel.CoverColors)) Configure(); }
    private void PropertyChangedAppearance(object? sender, PropertyChangedEventArgs e) => Configure();
    private void Configure()
    {
        if (model is null || closed) return;
        painter.Configure(model.Appearance.SelectedStyle, model.Appearance.SelectedLook, ActualThemeVariant == ThemeVariant.Dark, model.CoverColors);
        InvalidateVisual();
    }
    private void Changed()
    {
        if (model is null || closed) return;
        var current = model.Document.FindLine(model.PositionMs(false));
        if (line == current) return;
        line = current; InvalidateVisual();
    }
    public void UpdateClock(double positionMs, bool animate)
    {
        if (closed || !animate || model?.Appearance.SelectedStyle.Layout != LyricsLayout.Light || Stopwatch.GetElapsedTime(lastFrame).TotalMilliseconds < 34) return;
        lastFrame = Stopwatch.GetTimestamp(); phase = Math.Sin(positionMs / 5000) * 48; InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    { if (model is not null && !closed) painter.Draw(context, new Rect(Bounds.Size), model.Cover, line, phase); }
    public void Dispose()
    {
        if (closed) return; closed = true;
        if (model is not null)
        { model.Changed -= Changed; model.PropertyChanged -= PropertyChangedModel; model.Appearance.PropertyChanged -= PropertyChangedAppearance; }
        model = null;
    }
}
