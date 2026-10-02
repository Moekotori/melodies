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
        }, NeedsFrame);
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
    { if (e.PropertyName == nameof(LyricsViewModel.Cover)) InvalidateVisual(); }
    private void OnAppearanceChanged(object? sender, PropertyChangedEventArgs e) => ResetLayout();
    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e) { if (motion?.UseSpatialMotion != true) shift = 0; Refresh(); }
    private void ResetLayout()
    {
        cache.Dispose(); headerKey = null;
        if (model is not null)
        {
            var appearance = model.Appearance; var current = appearance.SelectedStyle;
            var dark = ActualThemeVariant == ThemeVariant.Dark;
            ink = LyricsStagePainter.Brush(appearance.IsAfterglow ? appearance.SelectedLook.Ink : current.Ink.Length == 0 ? dark ? "#EBEFF5" : "#292E37" : current.Ink);
            accent = LyricsStagePainter.Brush(appearance.IsAfterglow ? appearance.SelectedLook.Accent : current.Accent.Length == 0 ? dark ? "#A8D6FF" : "#4B55E8" : current.Accent);
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
            || model?.HasLyrics == true && model.IsPlaying && model.CanInterpolate && model.Appearance.WordHighlight);
    public override void Render(DrawingContext dc)
    {
        if (model is null || Bounds.Width < 1 || Bounds.Height < 1) return;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (dpi.X != scale) { dpi = new(scale, scale); cache.Dispose(); }
        var document = model.Document; var appearance = model.Appearance;
        var current = appearance.SelectedStyle;
        var next = document.FindLine(model.PositionMs(motion?.CanAnimate == true));
        if (next != active) { active = next; BrowseChanged?.Invoke(); }
        var w = Bounds.Width; var h = Bounds.Height;
        var textRect = TextArea(current.Layout, w, h);
        DrawHeader(dc, textRect, current.Layout);
        hits.Clear();
        if (document.Lines.Count == 0)
        {
            LyricsStagePainter.Label(dc, model.IsLoading ? "正在读取歌词…" : "还没有歌词", textRect.X, h * 0.44, 28, ink);
            return;
        }
        var index = DisplayIndex;
        var activeSize = Math.Clamp(appearance.FontSize, 18, 72) * current.Scale;
        var entry = Layout(index, activeSize, textRect.Width);
        if (entry.Height > h * 0.52) { activeSize = Math.Max(18, activeSize * h * 0.5 / entry.Height); entry = Layout(index, activeSize, textRect.Width); }
        var center = h * (current.Layout == LyricsLayout.Cinema ? 0.56 : 0.50);
        var elapsed = shift == 0 ? 220 : Stopwatch.GetElapsedTime(transition).TotalMilliseconds;
        var drift = motion?.UseSpatialMotion == true ? shift * Math.Pow(1 - Math.Clamp(elapsed / 220, 0, 1), 3) : 0;
        var y = center - entry.Height / 2 + drift;
        if (appearance.Context && current.Layout is not (LyricsLayout.Afterglow or LyricsLayout.WordFocus))
        {
            var above = y;
            for (var i = index - 1; i >= Math.Max(0, index - 4); i--)
            {
                var neighbor = Layout(i, appearance.FontSize * 0.75, textRect.Width);
                above -= neighbor.Height + 26;
                if (above < h * 0.15) break;
                DrawLine(dc, i, neighbor, new(textRect.X, above), false, 0.22 + (i == index - 1 ? 0.12 : 0));
            }
        }
        var isActive = browse < 0 && document.IsSynced && active >= 0;
        if (current.Layout == LyricsLayout.Poster)
        {
            using (dc.PushTransform(Matrix.CreateRotation(-0.018) * Matrix.CreateTranslation(0, 0)))
                DrawLine(dc, index, entry, new(textRect.X, y), isActive, 1);
        }
        else DrawLine(dc, index, entry, new(textRect.X, y), isActive, 1);
        if (appearance.Context && current.Layout != LyricsLayout.Afterglow)
        {
            var below = y + entry.Height + 30;
            var count = current.Layout == LyricsLayout.WordFocus ? 1 : 4;
            for (var i = index + 1; i <= Math.Min(document.Lines.Count - 1, index + count); i++)
            {
                var neighbor = Layout(i, appearance.FontSize * 0.75, textRect.Width);
                if (below + neighbor.Height > h * 0.94) break;
                DrawLine(dc, i, neighbor, new(textRect.X, below), false, i == index + 1 ? 0.43 : 0.22);
                below += neighbor.Height + 26;
            }
        }
        frames.Update(attached && motion?.IsWindowVisible == true, focused);
    }
    private LyricsTextLayoutCache.Entry Layout(int index, double size, double width)
    {
        var a = model!.Appearance; var style = a.SelectedStyle;
        var font = a.Font == "跟随样式" ? style.Layout == LyricsLayout.Poster ? "Bahnschrift, Microsoft YaHei, Noto Sans CJK SC" :
            style.Layout == LyricsLayout.Editorial || a.IsAfterglow && a.SelectedLook.Id == "paper" ? "SimSun, Songti SC, Noto Serif CJK SC" : "Segoe UI, Microsoft YaHei, Noto Sans CJK SC" : a.Font;
        var alignment = a.Alignment switch { "居中" => TextAlignment.Center, "右对齐" => TextAlignment.Right, "左对齐" => TextAlignment.Left, _ => style.Center ? TextAlignment.Center : TextAlignment.Left };
        return cache.Get(model.Document.Lines[index], new Typeface(font, weight: style.Layout == LyricsLayout.Poster ? FontWeight.Bold : FontWeight.Medium),
            size, Math.Clamp(a.SecondarySize, 10, 32), width, alignment, ink, accent, a.Translation, a.Romanization);
    }
    private void DrawLine(DrawingContext dc, int index, LyricsTextLayoutCache.Entry layout, Point point, bool current, double opacity)
    {
        hits.Add((index, new Rect(point, new Size(Bounds.Width - point.X - 20, layout.Height + 18))));
        using var alpha = dc.PushOpacity(opacity);
        layout.Primary.Draw(dc, point);
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
        using var secondaryAlpha = dc.PushOpacity(0.65);
        layout.Pronunciation?.Draw(dc, new(point.X, y));
        if (layout.Pronunciation is not null) y += layout.Pronunciation.Height + 8;
        layout.Secondary?.Draw(dc, new(point.X, y));
    }
    private void DrawHeader(DrawingContext dc, Rect area, LyricsLayout layout)
    {
        var width = Math.Max(32, area.Width); var font = new Typeface("Segoe UI, Microsoft YaHei, Noto Sans CJK SC");
        var key = (model!.Title, model.Artist, model.Album, width);
        if (headerKey != key || headerTitle is null)
        {
            headerTitle?.Dispose(); headerArtist?.Dispose(); headerKey = key;
            headerTitle = new(model.Title, font, 17, ink, maxWidth: width, maxLines: 1, textTrimming: TextTrimming.CharacterEllipsis);
            headerArtist = new(string.Join(" · ", new[] { model.Artist, model.Album }.Where(s => s.Length > 0)), font, 12, ink, maxWidth: width, maxLines: 1, textTrimming: TextTrimming.CharacterEllipsis);
        }
        var y = Bounds.Height * (layout == LyricsLayout.Cinema ? 0.16 : 0.065);
        headerTitle.Draw(dc, new(area.X, y)); using var opacity = dc.PushOpacity(0.6); headerArtist!.Draw(dc, new(area.X, y + 26));
    }
    private static Rect TextArea(LyricsLayout layout, double w, double h) => layout switch
    {
        LyricsLayout.CoverLeft when w >= 780 => new(w * 0.46, 0, w * 0.47, h),
        LyricsLayout.CoverRight when w >= 780 => new(w * 0.055, 0, w * 0.50, h),
        LyricsLayout.Vinyl when w >= 780 => new(w * 0.07, 0, w * 0.52, h),
        LyricsLayout.Editorial => new(w * 0.25, 0, w * 0.67, h),
        LyricsLayout.Poster or LyricsLayout.Slices => new(w * 0.12, 0, w * 0.77, h),
        _ => new(w * 0.08, 0, w * 0.84, h)
    };
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
        if (closed) return; closed = true; frames.Dispose(); cache.Dispose();
        headerTitle?.Dispose(); headerArtist?.Dispose();
        if (model is not null) { model.Changed -= Refresh; model.PropertyChanged -= OnModelPropertyChanged; model.Appearance.PropertyChanged -= OnAppearanceChanged; }
        if (motion is not null) motion.PropertyChanged -= OnMotionChanged;
        model = null; BrowseChanged = null; FrameRequested = null;
    }
}
