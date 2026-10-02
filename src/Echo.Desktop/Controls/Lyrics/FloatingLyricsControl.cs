using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Echo.Contracts.Lyrics;
using Echo.Desktop.Features.Floating;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Controls.Lyrics;

// A current line and one secondary row. Cached glyph clips, one visible-window frame driver.
public sealed class FloatingLyricsControl : Control, IDisposable
{
    private readonly LyricsTextLayoutCache layouts = new();
    private readonly LyricsTextLayoutCache shadows = new();
    private readonly LyricsFrameController frames;
    private LyricsViewModel? lyrics;
    private FloatingWindowsViewModel? settings;
    private MotionPolicy? motion;
    private IBrush ink = Brushes.White, accent = Brushes.LightBlue;
    private Typeface font = new("Microsoft YaHei, sans-serif", FontStyle.Normal, FontWeight.SemiBold);
    private bool attached, focused, disposed;
    private LyricsDocument? document;
    private LyricLine? fallbackRow, secondaryRow;
    private static readonly Vector[] OutlineOffsets = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1), new(1, 2)];
    public bool IsFrameDriverRunning => frames.IsRunning;
    public int LayoutCount => layouts.Count + shadows.Count;
    public FloatingLyricsControl()
    {
        ClipToBounds = true;
        frames = new(InvalidateVisual, NeedsFrame, () => motion?.MaximumFrameRate ?? 60);
        AttachedToVisualTree += (_, _) => { attached = true; Refresh(); };
        DetachedFromVisualTree += (_, _) => { attached = false; frames.Update(false, false); };
        SizeChanged += (_, _) => Clear();
    }
    public void Bind(LyricsViewModel model, FloatingWindowsViewModel preferences, MotionPolicy policy)
    {
        lyrics = model; settings = preferences; motion = policy;
        model.Changed += Refresh; preferences.PropertyChanged += PreferencesChanged; policy.PropertyChanged += MotionChanged;
        ReadColors(); Refresh();
    }
    public void SetFocused(bool value) { focused = value; Refresh(); }
    private bool NeedsFrame() => !disposed && attached && IsVisible && motion?.CanAnimate == true
        && lyrics is { HasLyrics: true, IsPlaying: true, CanInterpolate: true };
    private void MotionChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void PreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FloatingWindowsViewModel.Color) or nameof(FloatingWindowsViewModel.HighlightColor)
            or nameof(FloatingWindowsViewModel.FontFamily) or nameof(FloatingWindowsViewModel.FontSize)
            or nameof(FloatingWindowsViewModel.SecondarySize) or nameof(FloatingWindowsViewModel.DoubleLine)
            or nameof(FloatingWindowsViewModel.Translation) or nameof(FloatingWindowsViewModel.Romanization))
        { ReadColors(); Clear(); }
        else Refresh();
    }
    private void ReadColors()
    {
        if (settings is null) return;
        ink = new SolidColorBrush(Color.TryParse(settings.Color, out var color) ? color : Colors.White);
        accent = new SolidColorBrush(Color.TryParse(settings.HighlightColor, out color) ? color : Colors.LightBlue);
        try
        {
            var family = settings.FontFamily.Length <= 256 && settings.FontFamily.Length > 0 ? settings.FontFamily : "Microsoft YaHei, sans-serif";
            font = new(family, FontStyle.Normal, FontWeight.SemiBold);
        }
        catch (ArgumentException) { font = new("Microsoft YaHei, sans-serif", FontStyle.Normal, FontWeight.SemiBold); }
        catch (UriFormatException) { font = new("Microsoft YaHei, sans-serif", FontStyle.Normal, FontWeight.SemiBold); }
    }
    private void Clear() { layouts.Dispose(); shadows.Dispose(); Refresh(); }
    private void Refresh()
    {
        if (disposed) return;
        if (lyrics is not null && !ReferenceEquals(document, lyrics.Document))
        { document = lyrics.Document; layouts.Dispose(); shadows.Dispose(); }
        InvalidateVisual(); frames.Update(attached && IsVisible && motion?.IsWindowVisible == true, focused);
    }
    public override void Render(DrawingContext dc)
    {
        if (disposed || lyrics is null || settings is null || Bounds.Width < 32) return;
        var position = lyrics.PositionMs(motion?.CanAnimate == true) + settings.OffsetMs;
        var doc = lyrics.Document; var index = doc.FindLine(position);
        LyricLine line;
        if (doc.Lines.Count == 0) line = CachedRow(ref fallbackRow, lyrics.Player.Text.Translate(lyrics.IsLoading ? "正在读取歌词…" : "还没有歌词"));
        else if (!doc.IsSynced) { index = 0; line = doc.Lines[0]; }
        else if (index < 0) line = CachedRow(ref fallbackRow, lyrics.Title);
        else line = doc.Lines[index];
        var size = Math.Clamp(double.IsFinite(settings.FontSize) ? settings.FontSize : 32, 16, 72);
        var secondary = Math.Clamp(double.IsFinite(settings.SecondarySize) ? settings.SecondarySize : 20, 10, 40);
        var text = layouts.Get(line, font, size, secondary, Bounds.Width, TextAlignment.Center, ink, accent, false, false);
        var shadow = shadows.Get(line, font, size, secondary, Bounds.Width, TextAlignment.Center, Brushes.Black, Brushes.Black, false, false);
        var point = new Point(0, 2);
        DrawOutline(dc, shadow, point); text.Primary.Draw(dc, point);
        if (index >= 0 && doc.IsSynced && line.Words.Count > 0)
        {
            foreach (var word in text.Words)
            {
                var progress = Math.Clamp((position + doc.OffsetMs - word.Word.StartMs) / Math.Max(1, word.Word.EndMs - word.Word.StartMs), 0, 1);
                var width = Math.Clamp(progress * word.TotalWidth - word.PrefixWidth, 0, word.Bounds.Width);
                if (width <= 0) continue;
                var rect = word.Bounds.Translate(new Vector(point.X, point.Y));
                var x = word.Direction == FlowDirection.RightToLeft ? rect.Right - width : rect.X;
                using (dc.PushClip(new Rect(x, rect.Y, width, rect.Height))) text.Highlight.Draw(dc, point);
            }
        }
        else if (index >= 0 && doc.IsSynced) text.Highlight.Draw(dc, point);
        if (!settings.DoubleLine) return;
        var secondaryText = settings.Translation && !string.IsNullOrWhiteSpace(line.Translation) ? line.Translation
            : settings.Romanization && !string.IsNullOrWhiteSpace(line.Romanization) ? line.Romanization
            : index >= 0 && index + 1 < doc.Lines.Count ? doc.Lines[index + 1].Text : "";
        if (string.IsNullOrEmpty(secondaryText)) return;
        // Reuse the next line identity; synthetic translated rows are cached by record value.
        var row = CachedRow(ref secondaryRow, secondaryText);
        var next = layouts.Get(row, font, secondary, secondary, Bounds.Width, TextAlignment.Center, ink, ink, false, false);
        var nextShadow = shadows.Get(row, font, secondary, secondary, Bounds.Width, TextAlignment.Center, Brushes.Black, Brushes.Black, false, false);
        var below = new Point(0, text.Primary.Height + 12);
        using (dc.PushOpacity(.8)) { DrawOutline(dc, nextShadow, below); next.Primary.Draw(dc, below); }
    }
    private static void DrawOutline(DrawingContext dc, LyricsTextLayoutCache.Entry layout, Point point)
    {
        foreach (var offset in OutlineOffsets)
            layout.Primary.Draw(dc, point + offset);
    }
    private static LyricLine CachedRow(ref LyricLine? cached, string text)
    {
        if (cached?.Text != text) cached = new(text, 0, 0, Array.Empty<LyricWord>());
        return cached;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; frames.Dispose(); layouts.Dispose(); shadows.Dispose();
        if (lyrics is not null) lyrics.Changed -= Refresh;
        if (settings is not null) settings.PropertyChanged -= PreferencesChanged;
        if (motion is not null) motion.PropertyChanged -= MotionChanged;
        lyrics = null; settings = null; motion = null;
    }
}
