using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Controls.Lyrics;

// No lyric child controls or per-word bindings. Only the visible neighborhood is shaped and drawn.
public sealed partial class LyricsViewportControl : Control, IDisposable
{
    private readonly LyricsTextLayoutCache cache = new();
    private readonly LyricsWordPainter wordPainter = new();
    private readonly KineticPosterLayout poster = new();
    private readonly CurtainLyricsLayout curtain = new();
    private readonly KineticLyricsChrome kinetic = new();
    private readonly CutBoardTextLayout cutBoard = new();
    private readonly AfterglowGlyphLayout afterglowGlyphs = new();
    private readonly LyricsFrameController frames;
    private readonly List<(int Index, Rect Rect)> hits = [];
    private LyricsViewModel? model;
    private MotionPolicy? motion;
    private IBrush ink = Brushes.Black, accent = Brushes.Blue;
    private int active = -1, browse = -1;
    private bool attached, closed, focused = true;
    private long transition;
    private bool sceneFallback;
    public void SetSceneFallback(bool failed) { sceneFallback = failed; InvalidateVisual(); }
    private double shift;
    private double transitionMs = 360;
    private readonly Vector dpiSentinel = new(-1, -1);
    private readonly BlurEffect contextBlur = new() { Radius = 1.5 };
    private readonly Pen rail = new(Brushes.Transparent, 2);
    private Vector dpi;
    private Echo.Contracts.Lyrics.LyricsDocument? currentDocument;
    private Avalonia.Media.TextFormatting.TextLayout? headerTitle, headerArtist;
    private (string Title, string Artist, string Album, double Width, double Size, bool Center)? headerKey;
    public int LayoutCount => cache.Count;
    public bool IsFrameDriverRunning => frames.IsRunning;
    public bool IsFollowing => browse < 0;
    public int DisplayIndex => browse >= 0 ? browse : Math.Max(0, active);
    public event Action? BrowseChanged;
    public event Action<double, bool>? FrameRequested;
    public LyricsViewportControl()
    {
        Focusable = true; ClipToBounds = true; dpi = dpiSentinel;
        frames = new(() =>
        {
            InvalidateVisual();
            if (model is not null) FrameRequested?.Invoke(model.PositionMs(true), focused && motion?.UseSpatialMotion == true && model.IsPlaying);
        }, NeedsFrame, () => motion?.MaximumFrameRate ?? 60);
        AttachedToVisualTree += (_, _) => { attached = true; Refresh(); };
        DetachedFromVisualTree += (_, _) => Dispose();
        SizeChanged += (_, _) => ResetLayout();
        ActualThemeVariantChanged += (_, _) => ResetLayout();
        AutomationProperties.SetName(this, "歌词。方向键滚动，回车跳转，Home 恢复跟随播放。");
    }
    public void Bind(LyricsViewModel value, MotionPolicy policy)
    {
        model = value; motion = policy;
        model.Changed += Refresh; model.PropertyChanged += OnModelPropertyChanged;
        model.Appearance.PropertyChanged += OnAppearanceChanged; motion.PropertyChanged += OnMotionChanged;
        Refresh();
    }
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(LyricsViewModel.Cover) or nameof(LyricsViewModel.CoverColors)) ResetLayout(); }
    private void OnAppearanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricsAppearance.SelectedStyle)) transition = Stopwatch.GetTimestamp();
        ResetLayout();
    }
    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e) { if (motion?.UseSpatialMotion != true) shift = 0; Refresh(); }
    private void ResetLayout()
    {
        cache.Dispose(); poster.Dispose(); cutBoard.Dispose(); afterglowGlyphs.Dispose(); curtain.Dispose(); kinetic.Dispose(); headerKey = null;
        if (model is not null)
        {
            var appearance = model.Appearance; var current = appearance.SelectedStyle;
            var dark = ActualThemeVariant == ThemeVariant.Dark;
            var palette = LyricsStagePalette.Resolve(current, appearance.SelectedLook, model.CoverColors, dark,appearance.BackgroundMode);
            ink = new SolidColorBrush(palette.Ink); accent = new SolidColorBrush(palette.Accent); rail.Brush = accent;
        }
        Refresh();
    }
    public void SetFocused(bool value) { focused = value; Refresh(); }
    public void Follow() { browse = -1; shift = 0; BrowseChanged?.Invoke(); Refresh(); }
    public void BrowseTo(int index)
    {
        if (model is null || model.Document.Lines.Count == 0) return;
        browse = Math.Clamp(index, 0, model.Document.Lines.Count - 1); shift = 0; BrowseChanged?.Invoke(); Refresh();
    }
    private void Refresh()
    {
        if (closed || model is null) return;
        if (ink == Brushes.Black) { ResetLayout(); return; }
        var next = model.Document.FindLine(model.PositionMs(false));
        if (!ReferenceEquals(currentDocument, model.Document))
        { currentDocument = model.Document; browse = -1; active = -1; cache.Dispose(); shift = 0; }
        Advance(next);
        InvalidateVisual(); frames.Update(attached && motion?.IsWindowVisible == true, focused);
    }
    private bool NeedsFrame() => !closed && attached && motion?.IsWindowVisible == true && motion.CanAnimate
        && model?.Appearance.SelectedStyle.Layout is not (LyricsLayout.CoverOnly or LyricsLayout.SourceScene)
        && (model?.Appearance.SelectedStyle.Layout == LyricsLayout.KineticChars && Stopwatch.GetElapsedTime(transition).TotalMilliseconds < 1020
            || shift != 0 && Stopwatch.GetElapsedTime(transition).TotalMilliseconds < transitionMs
            || model?.HasLyrics == true && model.IsPlaying && model.CanInterpolate && (model.Appearance.WordHighlight || model.Appearance.IsAfterglow));
    public override void Render(DrawingContext dc)
    {
        if (model is null || Bounds.Width < 1 || Bounds.Height < 1) return;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (dpi.X != scale) { dpi = new(scale, scale); cache.Dispose(); }
        var document = model.Document; var appearance = model.Appearance;
        if (appearance.SelectedStyle.Layout == LyricsLayout.SourceScene && document.IsSynced && !sceneFallback)
        { hits.Clear(); frames.Update(false, false); return; }
        if (!appearance.Enabled) { hits.Clear(); frames.Update(false, false); return; }
        var current = appearance.SelectedStyle;
        if (current.Layout == LyricsLayout.Afterglow && document.Lines.Count > 0) { hits.Clear(); return; }
        var next = document.FindLine(model.PositionMs(motion?.CanAnimate == true));
        Advance(next);
        var w = Bounds.Width; var h = Bounds.Height;
        var textRect = LyricsStyleMetrics.TextArea(current.Layout, w, h);
        if (!appearance.HideSongInfo) DrawHeader(dc, textRect, current.Layout);
        hits.Clear();
        if (current.Layout == LyricsLayout.CoverOnly)
        { frames.Update(attached && motion?.IsWindowVisible == true, focused); return; }
        if (document.Lines.Count == 0)
        {
            LyricsStagePainter.Label(dc, model.Player.Text.Translate(model.IsLoading ? "正在读取歌词…" : "还没有歌词"), textRect.X, h * 0.44, 28, ink);
            return;
        }
        var index = DisplayIndex;
        if (current.Layout == LyricsLayout.Curtain)
        {
            curtain.Draw(dc, document, index, model.PositionMs(motion?.CanAnimate == true) + document.OffsetMs,
                new Rect(0, 0, w, h), appearance, ink, accent, (i, rect) => hits.Add((i, rect)));
            frames.Update(attached && motion?.IsWindowVisible == true, focused); return;
        }
        if (current.Layout == LyricsLayout.KineticChars) kinetic.DrawStage(dc, w, h, ink);
        if (current.Layout == LyricsLayout.WordFocus && browse < 0 && appearance.WordHighlight && document.IsSynced)
        {
            var glyphMetrics = LyricsStyleMetrics.Line(current.Layout, document.Lines[index], appearance, w, h, true);
            if (afterglowGlyphs.Draw(dc, document.Lines[index], model.PositionMs(motion?.CanAnimate == true) + document.OffsetMs,
                new Rect(textRect.X, h * .18, textRect.Width, h * .55), glyphMetrics.Size, ink, motion?.UseSpatialMotion != true))
            {
                var caption = Layout(index, glyphMetrics.Size, textRect.Width);
                caption.Secondary?.Draw(dc, new(textRect.X, h * .67)); hits.Add((index, new Rect(textRect.X, h * .18, textRect.Width, h * .55))); return;
            }
        }
        if (current.Layout == LyricsLayout.Slices && browse < 0 && document.Lines[index].Words.Count > 0)
        {
            cutBoard.Draw(dc, document.Lines[index], model.PositionMs(motion?.CanAnimate == true) + document.OffsetMs,
                new Rect(36, 0, w - 72, h * .72), ink, accent);
            cutBoard.DrawSides(dc, index > 0 ? document.Lines[index - 1].Text : null,
                index + 1 < document.Lines.Count ? document.Lines[index + 1].Text : null, new Rect(0, 0, w, h));
            hits.Add((index, new Rect(36, 0, w - 72, h * .72))); return;
        }
        var wordPoster = current.Layout == LyricsLayout.Poster && browse < 0 && document.IsSynced && document.Lines[index].Words.Count > 0;
        var metrics = LyricsStyleMetrics.Line(current.Layout, document.Lines[index], appearance, w, h, true);
        var activeSize = metrics.Size;
        var entry = Layout(index, activeSize, textRect.Width, true);
        if (entry.Height > h * 0.52) { activeSize = Math.Max(18, activeSize * h * 0.5 / entry.Height); entry = Layout(index, activeSize, textRect.Width); }
        var center = textRect.Y + textRect.Height * (current.Layout is LyricsLayout.Light or LyricsLayout.KineticChars ? .47 : current.Layout == LyricsLayout.Editorial ? .46 : .52);
        var elapsed = shift == 0 ? transitionMs : Stopwatch.GetElapsedTime(transition).TotalMilliseconds;
        var drift = motion?.UseSpatialMotion == true && transitionMs > 0
            ? shift * Math.Pow(1 - Math.Clamp(elapsed / transitionMs, 0, 1), 3) : 0;
        var railSpace = current.Layout == LyricsLayout.Light ? activeSize * .46 + Math.Clamp(h * .022, 14, 24) : 0;
        var rowHeight = Math.Max(entry.Height + railSpace + (current.Layout == LyricsLayout.Reading ? 14 : 0), metrics.MinimumHeight);
        var y = center - rowHeight / 2 + drift;
        if (current.Layout == LyricsLayout.Reading && index < 8)
            y = Math.Min(y, ReadingTop(index, textRect.Width, h) + drift);
        using DrawingContext.PushedState? fade = current.Layout is LyricsLayout.Light or LyricsLayout.Folded or LyricsLayout.KineticChars ? null
            : dc.PushOpacityMask(LyricsReadingPresentation.Fade, new Rect(0, textRect.Y, w, textRect.Height));
        if (appearance.Context && current.Layout is not (LyricsLayout.Afterglow or LyricsLayout.WordFocus))
        {
            var above = y;
            var neighbors = current.Layout is LyricsLayout.Light or LyricsLayout.KineticChars ? 1 : 4;
            for (var i = index - 1; i >= Math.Max(0, index - neighbors); i--)
            {
                var neighborMetrics = LyricsStyleMetrics.Line(current.Layout, document.Lines[i], appearance, w, h, false, index - i);
                var neighbor = Layout(i, neighborMetrics.Size, textRect.Width, false, index - i);
                above -= Math.Max(neighbor.Height, neighborMetrics.MinimumHeight) + metrics.Gap;
                if (above + neighbor.Height < textRect.Y) break;
                DrawContext(dc, i, neighbor, new(textRect.X, above), neighborMetrics, i - index);
            }
        }
        var isActive = browse < 0 && document.IsSynced && active >= 0;
        if (wordPoster)
        {
            poster.Draw(dc, document.Lines[index], model.PositionMs(motion?.CanAnimate == true) + document.OffsetMs,
                new Rect(textRect.X, y, textRect.Width, rowHeight), appearance.FontSize, ink, accent, w);
            hits.Add((index, new Rect(textRect.X, y, textRect.Width, rowHeight)));
            if (entry.Secondary is not null) entry.Secondary.Draw(dc, new(textRect.X, y + rowHeight + 8));
        }
        else DrawLine(dc, index, entry, new(textRect.X, y + (rowHeight - entry.Height - railSpace) / 2), isActive, 1);
        if (appearance.Context && current.Layout is not (LyricsLayout.Afterglow or LyricsLayout.WordFocus))
        {
            var below = y + rowHeight + (wordPoster ? 52 : metrics.Gap);
            var count = current.Layout is LyricsLayout.WordFocus or LyricsLayout.Light or LyricsLayout.KineticChars ? 1 : 4;
            for (var i = index + 1; i <= Math.Min(document.Lines.Count - 1, index + count); i++)
            {
                var neighborMetrics = LyricsStyleMetrics.Line(current.Layout, document.Lines[i], appearance, w, h, false, i - index);
                var neighbor = Layout(i, neighborMetrics.Size, textRect.Width, false, i - index);
                if (below > textRect.Bottom) break;
                DrawContext(dc, i, neighbor, new(textRect.X, below), neighborMetrics, i - index);
                below += Math.Max(neighbor.Height, neighborMetrics.MinimumHeight) + metrics.Gap;
            }
        }
        frames.Update(attached && motion?.IsWindowVisible == true, focused);
    }
    private void DrawContext(DrawingContext dc, int index, LyricsTextLayoutCache.Entry entry, Point point, LyricsLineMetrics metrics, int distance)
    {
        var opacity = LyricsReadingPresentation.Opacity(distance, model!.Appearance.ContextOpacityPercent);
        var rowHeight = Math.Max(entry.Height, metrics.MinimumHeight);
        var rowPoint = point;
        point += new Vector(0, (rowHeight - entry.Height) / 2);
        if (model.Appearance.SelectedStyle.Layout == LyricsLayout.KineticChars)
            point += new Vector(kinetic.DrawNeighbor(dc, point, Bounds.Width, distance < 0, ink, entry.Primary.TextLines[0].Baseline), 0);
        IDisposable? folded = null;
        if (model?.Appearance.SelectedStyle.Layout == LyricsLayout.Folded
            && LyricsStyleMetrics.TryFold(distance, Bounds.Width, Bounds.Height, out var pose))
            folded = dc.PushTransform(LyricsStyleMetrics.FoldMatrix(pose, rowPoint, LyricsStyleMetrics.TextArea(LyricsLayout.Folded, Bounds.Width, Bounds.Height).Width, rowHeight));
        try
        {
            if (model!.Appearance.ContextBlur)
            {
                contextBlur.Radius = LyricsReadingPresentation.Blur(distance);
                using (dc.PushEffect(contextBlur, new Rect(point, new Size(Bounds.Width - point.X, entry.Height)).Inflate(6)))
                    DrawLine(dc, index, entry, point, false, opacity);
            }
            else DrawLine(dc, index, entry, point, false, opacity);
        }
        finally { folded?.Dispose(); }
    }
    private void DrawLine(DrawingContext dc, int index, LyricsTextLayoutCache.Entry layout, Point point, bool current, double opacity)
    {
        IDisposable? folded = null;
        if (current && model?.Appearance.SelectedStyle.Layout == LyricsLayout.Folded
            && LyricsStyleMetrics.TryFold(0, Bounds.Width, Bounds.Height, out var pose))
        {
            var metrics = LyricsStyleMetrics.Line(LyricsLayout.Folded, model.Document.Lines[index], model.Appearance, Bounds.Width, Bounds.Height, true);
            var rowHeight = Math.Max(layout.Height, metrics.MinimumHeight);
            folded = dc.PushTransform(LyricsStyleMetrics.FoldMatrix(pose, point - new Vector(0, (rowHeight - layout.Height) / 2),
                LyricsStyleMetrics.TextArea(LyricsLayout.Folded, Bounds.Width, Bounds.Height).Width, rowHeight));
        }
        try
        {
        hits.Add((index, new Rect(point, new Size(Bounds.Width - point.X - 20, layout.Height + 18))));
        using var alpha = dc.PushOpacity(opacity);
        wordPainter.Paint(dc, layout, point, current && model!.Appearance.WordHighlight,
            model!.Appearance.SelectedStyle.Layout, model.PositionMs(motion?.CanAnimate == true) + model.Document.OffsetMs,
            current && motion?.UseSpatialMotion == true ? Stopwatch.GetElapsedTime(transition).TotalMilliseconds : double.PositiveInfinity);
        if (current && model!.Appearance.SelectedStyle.Layout == LyricsLayout.Editorial)
        {
            var dot = new Point(point.X - 28, point.Y + layout.Primary.Height / 2);
            using (dc.PushOpacity(0.08)) dc.DrawEllipse(accent, null, dot, 10, 10);
            dc.DrawEllipse(accent, null, dot, 4, 4);
        }
        if (current && model.Appearance.SelectedStyle.Layout == LyricsLayout.KineticChars)
            kinetic.DrawActive(dc, point, Bounds.Width - point.X * 2, model.Appearance.FontSize / 40 * Math.Clamp(Bounds.Width * .052, 40, 88), layout.Height, ink, motion?.UseSpatialMotion == true ? Stopwatch.GetElapsedTime(transition).TotalMilliseconds : double.PositiveInfinity);
        var y = point.Y + layout.Primary.Height + 10;
        if (current && model!.Appearance.SelectedStyle.Layout == LyricsLayout.Light)
        {
            var px = LyricsStyleMetrics.Line(LyricsLayout.Light, model.Document.Lines[index], model.Appearance, Bounds.Width, Bounds.Height, true).Size;
            var railY = point.Y + layout.Primary.Height + px * .46;
            var railWidth = Math.Clamp(Bounds.Width * .08, 64, 132);
            using (dc.PushOpacity(.8)) dc.DrawLine(rail, new(point.X, railY), new(point.X + railWidth, railY));
            y = railY + Math.Clamp(Bounds.Height * .022, 14, 24);
        }
        using var secondaryAlpha = dc.PushOpacity(0.65);
        layout.Pronunciation?.Draw(dc, new(point.X, y));
        if (layout.Pronunciation is not null) y += layout.Pronunciation.Height + 8;
        layout.Secondary?.Draw(dc, new(point.X, y));
        }
        finally { folded?.Dispose(); }
    }
    private void DrawHeader(DrawingContext dc, Rect area, LyricsLayout layout)
    {
        var place = LyricsStyleMetrics.Caption(layout, Bounds.Width, Bounds.Height, area.X, area.Width);
        var font = new Typeface(LyricsReadingPresentation.Sans, weight: place.Bold ? FontWeight.Bold : FontWeight.Normal);
        var key = (model!.Title, model.Artist, model.Album, place.Width, place.Size, place.Center);
        if (headerKey != key || headerTitle is null)
        {
            headerTitle?.Dispose(); headerArtist?.Dispose(); headerKey = key;
            var alignment = place.Center ? TextAlignment.Center : TextAlignment.Left;
            headerTitle = new(model.Title, font, place.Size, ink, textAlignment: alignment, maxWidth: place.Width, maxLines: place.Center ? 2 : 1, textTrimming: TextTrimming.CharacterEllipsis);
            headerArtist = new(string.Join(" · ", new[] { model.Artist, model.Album }.Where(s => s.Length > 0)), font, place.Center ? Math.Max(13, place.Size * .42) : 12, ink,
                textAlignment: alignment, maxWidth: place.Width, maxLines: 1, textTrimming: TextTrimming.CharacterEllipsis);
        }
        headerTitle.Draw(dc, new(place.X, place.Y));
        using var opacity = dc.PushOpacity(0.6);
        headerArtist!.Draw(dc, new(place.X, place.Y + (place.Center ? headerTitle.Height + 8 : place.Size + 10)));
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    { base.OnPointerWheelChanged(e); BrowseTo(DisplayIndex - Math.Sign(e.Delta.Y)); e.Handled = true; }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var hit = hits.FirstOrDefault(h => h.Rect.Contains(e.GetPosition(this)));
        if (hit.Rect.Width > 0 && model is not null) _ = model.SeekLineAsync(hit.Index);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Up: BrowseTo(DisplayIndex - 1); break;
            case Key.Down: BrowseTo(DisplayIndex + 1); break;
            case Key.PageUp: BrowseTo(DisplayIndex - 5); break;
            case Key.PageDown: BrowseTo(DisplayIndex + 5); break;
            case Key.Home: Follow(); break;
            case Key.End: BrowseTo((model?.Document.Lines.Count ?? 1) - 1); break;
            case Key.Enter: if (model is not null) _ = model.SeekLineAsync(DisplayIndex); break;
            default: return;
        }
        e.Handled = true;
    }
    public void Dispose()
    {
        if (closed) return; closed = true; frames.Dispose(); cache.Dispose(); poster.Dispose(); cutBoard.Dispose(); afterglowGlyphs.Dispose(); curtain.Dispose(); kinetic.Dispose();
        headerTitle?.Dispose(); headerArtist?.Dispose();
        if (model is not null) { model.Changed -= Refresh; model.PropertyChanged -= OnModelPropertyChanged; model.Appearance.PropertyChanged -= OnAppearanceChanged; }
        if (motion is not null) motion.PropertyChanged -= OnMotionChanged;
        model = null; BrowseChanged = null; FrameRequested = null;
    }
}
