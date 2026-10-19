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
    private readonly LyricsBackgroundOverride backgroundOverride = new();
    private LyricsViewModel? model;
    private int line = int.MinValue;
    private bool closed;
    private long lastFrame;
    private double phase = -1;
    private bool backgroundFallback;
    private bool? backgroundDark;
    public void SetBackgroundFallback(bool value){backgroundFallback=value;InvalidateVisual();}
    public void SetBackgroundTheme(bool dark){backgroundDark=dark;Configure();}
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
        backgroundOverride.Configure(backgroundDark??ActualThemeVariant == ThemeVariant.Dark);
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
        if (closed || model?.Appearance.SelectedStyle.Layout != LyricsLayout.Light) return;
        if (!animate) { if (phase >= 0) { phase = -1; InvalidateVisual(); } return; }
        if (Stopwatch.GetElapsedTime(lastFrame).TotalMilliseconds < 34) return;
        lastFrame = Stopwatch.GetTimestamp(); phase = positionMs; InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        if(model is null||closed)return;
        var overridden=!backgroundFallback&&model.Appearance.BackgroundMode!="page"&&model.Appearance.SelectedStyle.Layout is not (LyricsLayout.SourceScene or LyricsLayout.Afterglow);
        if(overridden)backgroundOverride.Draw(context,new Rect(Bounds.Size),model.Appearance,model.Cover);
        painter.Draw(context,new Rect(Bounds.Size),model.Cover,phase,model.Appearance.HideSongInfo,overridden);
    }
    public void Dispose()
    {
        if (closed) return; closed = true; painter.Dispose(); backgroundOverride.Dispose();
        if (model is not null)
        { model.Changed -= Changed; model.PropertyChanged -= PropertyChangedModel; model.Appearance.PropertyChanged -= PropertyChangedAppearance; }
        model = null;
    }
}
