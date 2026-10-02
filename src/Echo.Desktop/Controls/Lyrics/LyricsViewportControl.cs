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
public sealed class LyricsViewportControl : Control, IDisposable
{
    private readonly LyricsTextLayoutCache cache = new();
    private readonly KineticPosterLayout poster = new();
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
    private double shift;
    private readonly Vector dpiSentinel = new(-1, -1);
    private Vector dpi;
    private Echo.Contracts.Lyrics.LyricsDocument? currentDocument;
    private Avalonia.Media.TextFormatting.TextLayout? headerTitle, headerArtist;
    private (string Title, string Artist, string Album, double Width)? headerKey;
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
    private void OnAppearanceChanged(object? sender, PropertyChangedEventArgs e) => ResetLayout();
    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e) { if (motion?.UseSpatialMotion != true) shift = 0; Refresh(); }
    private void ResetLayout()
    {
        cache.Dispose(); poster.Dispose(); cutBoard.Dispose(); afterglowGlyphs.Dispose(); headerKey = null;
        if (model is not null)
        {
            var appearance = model.Appearance; var current = appearance.SelectedStyle;
            var dark = ActualThemeVariant == ThemeVariant.Dark;
            var palette = LyricsStagePalette.Resolve(current, appearance.SelectedLook, model.CoverColors, dark);
            ink = new SolidColorBrush(palette.Ink); accent = new SolidColorBrush(palette.Accent);
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
        { currentDocument = model.Document; browse = -1; cache.Dispose(); shift = 0; }
        if (next != active)
        {
            var difference = next - active;
            if (Math.Abs(difference) == 1 && active >= 0 && motion?.UseSpatialMotion == true && browse < 0)
            { shift = Math.Sign(difference) * (model.Appearance.FontSize + 48); transition = Stopwatch.GetTimestamp(); }
            else shift = 0;
            active = next; BrowseChanged?.Invoke();
        }
        InvalidateVisual(); frames.Update(attached && motion?.IsWindowVisible == true, focused);
    }
    private bool NeedsFrame() => !closed && attached && motion?.IsWindowVisible == true && motion.CanAnimate
        && (shift != 0 && Stopwatch.GetElapsedTime(transition).TotalMilliseconds < 220
            || model?.HasLyrics == true && model.IsPlaying && model.CanInterpolate && (model.Appearance.WordHighlight || model.Appearance.IsAfterglow));
    public override void Render(DrawingContext dc)
    {
        if (model is null || Bounds.Width < 1 || Bounds.Height < 1) return;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (dpi.X != scale) { dpi = new(scale, scale); cache.Dispose(); }
        var document = model.Document; var appearance = model.Appearance;
        if (!appearance.Enabled) { hits.Clear(); frames.Update(false, false); return; }
        var current = appearance.SelectedStyle;
        if (current.Layout == LyricsLayout.Afterglow && document.Lines.Count > 0) { hits.Clear(); return; }
        var next = document.FindLine(model.PositionMs(motion?.CanAnimate == true));
        if (next != active) { active = next; BrowseChanged?.Invoke(); }
        var w = Bounds.Width; var h = Bounds.Height;
        var textRect = LyricsStyleMetrics.TextArea(current.Layout, w, h);
        if (!appearance.HideSongInfo) DrawHeader(dc, textRect, current.Layout);
        hits.Clear();
        if (document.Lines.Count == 0)
        {
            LyricsStagePainter.Label(dc, model.Player.Text.Translate(model.IsLoading ? "正在读取歌词…" : "还没有歌词"), textRect.X, h * 0.44, 28, ink);
            return;
        }
        var index = DisplayIndex;
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
            hits.Add((index, new Rect(36, 0, w - 72, h * .72))); return;
        }
        var wordPoster = current.Layout == LyricsLayout.Poster && browse < 0 && document.IsSynced && document.Lines[index].Words.Count > 0;
        var metrics = LyricsStyleMetrics.Line(current.Layout, document.Lines[index], appearance, w, h, true);
        var activeSize = metrics.Size;
        var entry = Layout(index, activeSize, textRect.Width, true);
        if (entry.Height > h * 0.52) { activeSize = Math.Max(18, activeSize * h * 0.5 / entry.Height); entry = Layout(index, activeSize, textRect.Width); }
        var center = h * (current.Layout == LyricsLayout.Light ? .47 : current.Layout == LyricsLayout.Cinema ? .52 : .50);
        var elapsed = shift == 0 ? 220 : Stopwatch.GetElapsedTime(transition).TotalMilliseconds;
        var drift = motion?.UseSpatialMotion == true ? shift * Math.Pow(1 - Math.Clamp(elapsed / 220, 0, 1), 3) : 0;
        var railSpace = current.Layout == LyricsLayout.Light ? activeSize * .46 + Math.Clamp(h * .022, 14, 24) : 0;
        var rowHeight = Math.Max(entry.Height + railSpace, metrics.MinimumHeight);
        var y = center - rowHeight / 2 + drift;
        if (appearance.Context && current.Layout is not (LyricsLayout.Afterglow or LyricsLayout.WordFocus))
        {
            var above = y;
            var neighbors = current.Layout == LyricsLayout.Light ? 1 : 4;
            for (var i = index - 1; i >= Math.Max(0, index - neighbors); i--)
            {
                var neighborMetrics = LyricsStyleMetrics.Line(current.Layout, document.Lines[i], appearance, w, h, false, index - i);
                var neighbor = Layout(i, neighborMetrics.Size, textRect.Width, false, index - i);
                above -= Math.Max(neighbor.Height, neighborMetrics.MinimumHeight) + metrics.Gap;
                if (above < h * 0.15) break;
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
            var count = current.Layout is LyricsLayout.WordFocus or LyricsLayout.Light ? 1 : 4;
            for (var i = index + 1; i <= Math.Min(document.Lines.Count - 1, index + count); i++)
            {
                var neighborMetrics = LyricsStyleMetrics.Line(current.Layout, document.Lines[i], appearance, w, h, false, i - index);
                var neighbor = Layout(i, neighborMetrics.Size, textRect.Width, false, i - index);
                if (below + neighbor.Height > h * 0.80) break;
                DrawContext(dc, i, neighbor, new(textRect.X, below), neighborMetrics, i - index);
                below += Math.Max(neighbor.Height, neighborMetrics.MinimumHeight) + metrics.Gap;
            }
        }
        frames.Update(attached && motion?.IsWindowVisible == true, focused);
    }
    private LyricsTextLayoutCache.Entry Layout(int index, double size, double width, bool focus = true, int distance = 0)
    {
        var a = model!.Appearance; var style = a.SelectedStyle;
        var font = a.Font == "跟随样式" ? style.Layout == LyricsLayout.Poster ? "Bahnschrift, Microsoft YaHei, Noto Sans CJK SC" :
            style.Layout is LyricsLayout.Editorial or LyricsLayout.Folded or LyricsLayout.Slices || a.IsAfterglow && a.SelectedLook.Id == "paper" ? "Noto Serif SC, Noto Serif CJK SC, Songti SC, SimSun" : "Segoe UI, Microsoft YaHei, Noto Sans CJK SC" : a.Font;
        var alignment = a.Alignment switch { "居中" => TextAlignment.Center, "右对齐" => TextAlignment.Right, "左对齐" => TextAlignment.Left, _ => style.Center ? TextAlignment.Center : TextAlignment.Left };
        var metrics = LyricsStyleMetrics.Line(style.Layout, model.Document.Lines[index], a, Bounds.Width, Bounds.Height, focus, distance);
        if (a.MaxCharacters > 0) width = Math.Min(width, Math.Max(size * 3, a.MaxCharacters * size * .72));
        return cache.Get(model.Document.Lines[index], new Typeface(font, weight: metrics.Weight),
            size, metrics.SecondarySize, width, alignment, ink, accent, metrics.Secondary && a.Translation && (focus || !a.CurrentTranslationOnly),
            metrics.Secondary && a.Romanization, metrics.LineHeight * a.LineHeightPercent / 110, metrics.LetterSpacing);
    }
    private void DrawContext(DrawingContext dc, int index, LyricsTextLayoutCache.Entry entry, Point point, LyricsLineMetrics metrics, int distance)
    {
        using var contextAlpha = dc.PushOpacity(Math.Clamp((model?.Appearance.ContextOpacityPercent ?? 49) / 49, 0, 1));
        if (model?.Appearance.ContextBlur == true)
        {
            using var effect = dc.PushEffect(new BlurEffect { Radius = 1.5 }, new Rect(point, new Size(Bounds.Width - point.X, entry.Height)).Inflate(4));
            DrawLine(dc, index, entry, point, false, metrics.Opacity); return;
        }
        if (model?.Appearance.SelectedStyle.Layout == LyricsLayout.Folded)
        {
            var n = Math.Min(3, Math.Abs(distance)); var sign = Math.Sign(distance);
            var x = sign * Bounds.Width * (n == 1 ? .085 : n == 2 ? .135 : .175);
            var factor = n == 1 ? .76 : n == 2 ? .70 : .64;
            var z = n == 1 ? 150 : n == 2 ? 260 : 380;
            var scale = factor * 1500 / (1500 + z);
            using (dc.PushTransform(Matrix.CreateTranslation(-point.X, -point.Y) * Matrix.CreateScale(scale, scale)
                * Matrix.CreateRotation(sign * n * .017) * Matrix.CreateTranslation(point.X + x, point.Y)))
                DrawLine(dc, index, entry, point, false, metrics.Opacity * .16);
        }
        else DrawLine(dc, index, entry, point, false, metrics.Opacity);
    }
    private void DrawLine(DrawingContext dc, int index, LyricsTextLayoutCache.Entry layout, Point point, bool current, double opacity)
    {
        hits.Add((index, new Rect(point, new Size(Bounds.Width - point.X - 20, layout.Height + 18))));
        using var alpha = dc.PushOpacity(opacity);
        var upcomingOpacity = current && layout.Words.Count > 0 && model!.Appearance.WordHighlight
            ? model.Appearance.SelectedStyle.Layout switch { LyricsLayout.Light => 0.46, LyricsLayout.WordFocus => 0.44, LyricsLayout.CoverRight => 0.56, _ => 1 } : 1;
        using (dc.PushOpacity(upcomingOpacity)) layout.Primary.Draw(dc, point);
        if (current && model!.Appearance.SelectedStyle.Layout == LyricsLayout.Editorial)
        {
            var dot = new Point(point.X - 28, point.Y + layout.Primary.Height / 2);
            using (dc.PushOpacity(0.08)) dc.DrawEllipse(accent, null, dot, 10, 10);
            dc.DrawEllipse(accent, null, dot, 4, 4);
        }
        if (current && model!.Appearance.WordHighlight)
        {
            var position = model.PositionMs(motion?.CanAnimate == true) + model.Document.OffsetMs;
            if (layout.Words.Count > 0)
            {
                foreach (var word in layout.Words)
                {
                    var progress = Math.Clamp((position - word.Word.StartMs) / Math.Max(1, word.Word.EndMs - word.Word.StartMs), 0, 1);
                    if (progress <= 0) continue;
                    var rect = word.Bounds.Translate(new Vector(point.X, point.Y));
                    var filled = Math.Clamp(progress * word.TotalWidth - word.PrefixWidth, 0, rect.Width);
                    var x = word.Direction == FlowDirection.RightToLeft ? rect.Right - filled : rect.X;
                    var lift = model.Appearance.SelectedStyle.Layout is LyricsLayout.WordFocus or LyricsLayout.Afterglow
                        && motion?.UseSpatialMotion == true ? -Math.Sin(Math.PI * progress) * 3 : 0;
                    using (dc.PushTransform(Matrix.CreateTranslation(0, lift)))
                    using (dc.PushClip(new Rect(x, rect.Y, filled, rect.Height))) layout.Highlight.Draw(dc, point);
                }
            }
            else layout.Highlight.Draw(dc, point);
        }
        var y = point.Y + layout.Primary.Height + 10;
        if (current && model!.Appearance.SelectedStyle.Layout == LyricsLayout.Light)
        {
            var px = LyricsStyleMetrics.Line(LyricsLayout.Light, model.Document.Lines[index], model.Appearance, Bounds.Width, Bounds.Height, true).Size;
            var railY = point.Y + layout.Primary.Height + px * .46;
            var railWidth = Math.Clamp(Bounds.Width * .08, 64, 132);
            using (dc.PushOpacity(.8)) dc.DrawLine(new Pen(accent, 2), new(point.X, railY), new(point.X + railWidth, railY));
            y = railY + Math.Clamp(Bounds.Height * .022, 14, 24);
        }
        using var secondaryAlpha = dc.PushOpacity(0.65);
        layout.Pronunciation?.Draw(dc, new(point.X, y));
        if (layout.Pronunciation is not null) y += layout.Pronunciation.Height + 8;
        layout.Secondary?.Draw(dc, new(point.X, y));
    }
    private void DrawHeader(DrawingContext dc, Rect area, LyricsLayout layout)
    {
        var width = Math.Max(32, area.Width); var font = new Typeface("Segoe UI, Microsoft YaHei, Noto Sans CJK SC");
        var x = area.X; var y = Math.Clamp(Bounds.Height * 0.04, 29, 47); var size = 17d;
        if (layout == LyricsLayout.Light)
        { x = Math.Clamp(Bounds.Width * 0.07, 54, 148) + 70; y = Bounds.Height - Math.Clamp(Bounds.Height * 0.13, 96, 140) - 44; width = 440; }
        else if (layout == LyricsLayout.CoverLeft && Bounds.Width >= 780)
        { x = Math.Clamp(Bounds.Width * 0.048, 36, 86); y = Bounds.Height - Math.Clamp(Bounds.Height * .11, 88, 130) - 58; width = Math.Min(620, Bounds.Width * 0.36); size = Math.Clamp(Bounds.Width * .0135, 18, 24); }
        else if (layout == LyricsLayout.Editorial && Bounds.Width >= 780)
        { x = Math.Clamp(Bounds.Width * 0.031, 40, 56); y = Bounds.Height * 0.25 + Math.Clamp(Bounds.Width * 0.21, 250, 312) + 28; width = Math.Clamp(Bounds.Width * 0.21, 250, 312); size = 30; font = new(font.FontFamily, weight: FontWeight.Bold); }
        else if (layout == LyricsLayout.CoverRight) { y = Math.Clamp(Bounds.Height * 0.086, 64, 104); size = 27; }
        else if (layout == LyricsLayout.Vinyl) { x = area.X; y = Math.Clamp(Bounds.Height * .08, 64, 104); size = Math.Clamp(Bounds.Width * .021, 24, 34); }
        else if (layout is LyricsLayout.Reading or LyricsLayout.WordFocus or LyricsLayout.Afterglow) y = Math.Max(82, Bounds.Height * 0.105);
        var key = (model!.Title, model.Artist, model.Album, width);
        if (headerKey != key || headerTitle is null)
        {
            headerTitle?.Dispose(); headerArtist?.Dispose(); headerKey = key;
            headerTitle = new(model.Title, font, size, ink, maxWidth: width, maxLines: 1, textTrimming: TextTrimming.CharacterEllipsis);
            headerArtist = new(string.Join(" · ", new[] { model.Artist, model.Album }.Where(s => s.Length > 0)), font, 12, ink, maxWidth: width, maxLines: 1, textTrimming: TextTrimming.CharacterEllipsis);
        }
        headerTitle.Draw(dc, new(x, y)); using var opacity = dc.PushOpacity(0.6); headerArtist!.Draw(dc, new(x, y + size + 10));
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
        if (closed) return; closed = true; frames.Dispose(); cache.Dispose(); poster.Dispose(); cutBoard.Dispose(); afterglowGlyphs.Dispose();
        headerTitle?.Dispose(); headerArtist?.Dispose();
        if (model is not null) { model.Changed -= Refresh; model.PropertyChanged -= OnModelPropertyChanged; model.Appearance.PropertyChanged -= OnAppearanceChanged; }
        if (motion is not null) motion.PropertyChanged -= OnMotionChanged;
        model = null; BrowseChanged = null; FrameRequested = null;
    }
}
