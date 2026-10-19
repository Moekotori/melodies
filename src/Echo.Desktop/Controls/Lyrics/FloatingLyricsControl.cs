using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.Threading;
using Echo.Contracts.Lyrics;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.Floating;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Controls.Lyrics;

// Electron-style karaoke strip: one played gradient, one unplayed gradient, optional stroke.
public sealed partial class FloatingLyricsControl : Control, IDisposable
{
    private static readonly Vector[] Outline = [new(-1.4, 0), new(1.4, 0), new(0, -1.4), new(0, 1.4), new(-1, -1), new(1, -1), new(-1, 1), new(1, 1)];
    private readonly LineCache cache = new();
    private readonly LyricsFrameController frames;
    private readonly Stopwatch marquee = Stopwatch.StartNew();
    private LyricsViewModel? lyrics;
    private FloatingWindowsViewModel? settings;
    private MotionPolicy? motion;
    private bool attached, focused, disposed, customFont;
    private int fontToken;
    private string marqueePrimary = "", marqueeSecondary = "";
    private bool secondaryRoman, secondaryTranslation;
    private int pairedIndex = int.MinValue, upperIndex = -1, lowerIndex = -1;
    private LyricLine? secondarySource;
    private int secondarySourceIndex = -1;
    private string shownTitle = "";
    private string shownArtist = "";
    private LyricsDocument? shownDocument;
    private IReadOnlyList<LyricLine> shown = [];
    private ThemeVariant? themeSeen;
    private Color themePlayed = Colors.White;
    private Color themeOutline = Color.Parse("#242730");
    private Typeface medium, regular;
    private FontBag? fonts;
    public bool IsFrameDriverRunning => frames.IsRunning;
    public int LayoutCount => cache.Count;
    public FloatingLyricsControl()
    {
        ClipToBounds = true;
        medium = regular = new(new FontFamily(DesktopLyricsFont.Default), FontStyle.Normal, FontWeight.Medium);
        regular = new(medium.FontFamily, FontStyle.Normal, FontWeight.Normal);
        frames = new(InvalidateVisual, NeedsFrame, () => Math.Min(30, motion?.MaximumFrameRate ?? 30));
        AttachedToVisualTree += (_, _) => { attached = true; Refresh(); };
        DetachedFromVisualTree += (_, _) => { attached = false; frames.Update(false, false); };
        SizeChanged += (_, _) => Refresh();
    }
    public void Bind(LyricsViewModel model, FloatingWindowsViewModel preferences, MotionPolicy policy)
    {
        lyrics = model; settings = preferences; motion = policy;
        model.Changed += Refresh; preferences.PropertyChanged += PreferencesChanged; policy.PropertyChanged += MotionChanged;
        ReadTypeface(); QueueFont(); Refresh();
    }
    public void SetFocused(bool value) { focused = value; Refresh(); }
    private bool NeedsFrame() => !disposed && attached && IsVisible && motion?.CanAnimate == true && lyrics is { HasLyrics: true, IsPlaying: true };
    private void MotionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
    private void PreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FloatingWindowsViewModel.FontFilePath)) QueueFont();
        if (e.PropertyName == nameof(FloatingWindowsViewModel.FontFamily)) { ReadTypeface(); cache.Dispose(); }
        if (e.PropertyName is nameof(FloatingWindowsViewModel.Romanization) or nameof(FloatingWindowsViewModel.Translation)
            or nameof(FloatingWindowsViewModel.DoubleLine)) ReadSecondaryTrack();
        Refresh();
    }
    private void Refresh()
    {
        if (disposed) return;
        InvalidateVisual();
        frames.Update(attached && IsVisible && motion?.IsWindowVisible == true, focused);
    }
    public override void Render(DrawingContext dc)
    {
        if (disposed || lyrics is null || settings is null || Bounds.Width < 32 || Bounds.Height < 24) return;
        ReadTheme();
        var doc = lyrics.Document;
        EnsureShown(doc);
        var position = lyrics.PositionMs(motion?.CanAnimate == true) + settings.OffsetMs + doc.OffsetMs;
        var duration = Math.Max(0, lyrics.Player.Duration * 1000);
        var scale = Math.Clamp(double.IsFinite(settings.ScalePercent) ? settings.ScalePercent : 100, 75, 170) / 100;
        var primarySize = Math.Clamp(settings.FontSize, 18, 72) * scale;
        var secondarySize = Math.Clamp(settings.SecondarySize, 12, 48) * scale;
        var vertical = settings.TextDirection == "vertical";
        var alignLeft = settings.TextAlign == "left";
        ResolveRows(position, duration, out var primary, out var secondary, out var primaryProgress, out var secondaryProgress);
        if (primary != marqueePrimary || secondary != marqueeSecondary)
        { marqueePrimary = primary; marqueeSecondary = secondary; marquee.Restart(); }
        var elapsed = motion?.CanAnimate == true && lyrics.IsPlaying ? marquee.Elapsed.TotalSeconds : 0;
        var primaryPaint = PaintFor(false);
        var secondaryPaint = PaintFor(true);
        var secondaryPx = secondarySize;
        var primaryLine = cache.Get(primary, medium, primarySize, primaryPaint, vertical);
        var primaryOpacity = settings.DoubleLine && !HasSecondary() && upperIndex != pairedIndex ? .74 : 1;
        var secondaryOpacity = settings.DoubleLine && !HasSecondary() && lowerIndex != pairedIndex ? .74 : 1;
        var secondaryLine = secondary.Length == 0 ? null : cache.Get(secondary, regular, secondaryPx, secondaryPaint, vertical);
        var gap = secondaryLine is null && settings.DoubleLine && !HasSecondary() ? secondarySize * DesktopLyricsLayout.LineHeight : 0;
        using (dc.PushOpacity(Math.Clamp(settings.Opacity, .35, 1)))
        {
            if (!vertical)
            {
                var block = primaryLine.Height + (secondaryLine?.Height ?? gap) + (secondaryLine is null ? 0 : DesktopLyricsLayout.LineGap);
                var y = Math.Max(0, (Bounds.Height - block) / 2);
                DrawLine(dc, primaryLine, primaryProgress, 0, y, Bounds.Width, alignLeft, elapsed, primaryOpacity);
                if (secondaryLine is not null) DrawLine(dc, secondaryLine, secondaryProgress, 0, y + primaryLine.Height + DesktopLyricsLayout.LineGap, Bounds.Width, alignLeft, elapsed, secondaryOpacity);
            }
            else
            {
                var secondaryWidth = secondaryLine?.Width ?? 0;
                var block = primaryLine.Width + (secondaryLine is null ? 0 : secondaryWidth + DesktopLyricsLayout.LineGap);
                var x = Math.Max(0, (Bounds.Width - block) / 2);
                if (secondaryLine is not null) DrawLine(dc, secondaryLine, secondaryProgress, x, 0, Bounds.Height, alignLeft, elapsed, secondaryOpacity);
                DrawLine(dc, primaryLine, primaryProgress, x + (secondaryLine is null ? 0 : secondaryWidth + DesktopLyricsLayout.LineGap), 0, Bounds.Height, alignLeft, elapsed, primaryOpacity);
            }
        }
    }
    private void ResolveRows(double position, double duration, out string primary, out string secondary, out double primaryProgress, out double secondaryProgress)
    {
        primaryProgress = secondaryProgress = 0; secondary = "";
        if (shown.Count == 0)
        {
            primary = lyrics!.IsLoading ? lyrics.MessageDisplay : DesktopLyricsLayout.PlayInfo(lyrics.Title, lyrics.Artist);
            if (primary.Length == 0) primary = "melodies";
            return;
        }
        var synced = lyrics!.Document.IsSynced;
        var index = synced ? DesktopLyricsLayout.SyncedIndex(shown, position) : DesktopLyricsLayout.PlainIndex(shown.Count, position, duration);
        var roman = secondaryRoman;
        var translation = secondaryTranslation;
        var secondaryTrack = HasSecondary();
        if (pairedIndex != index)
        {
            pairedIndex = index;
            var resolved = DesktopLyricsLayout.Pair(Math.Max(index, 0), shown.Count, settings!.DoubleLine, secondaryTrack);
            if (index < 0) resolved = new(0, settings.DoubleLine && !secondaryTrack && shown.Count > 1 ? 1 : -1);
            upperIndex = resolved.Upper; lowerIndex = resolved.Lower;
            secondarySource = secondaryTrack ? DesktopLyricsLayout.SecondarySource(shown, index, roman, translation) : null;
            secondarySourceIndex = secondarySource is null ? -1 : IndexOf(secondarySource);
        }
        var pair = new DesktopLyricsLayout.Pairing(upperIndex, lowerIndex);
        var upper = pair.Upper >= 0 && pair.Upper < shown.Count ? shown[pair.Upper] : null;
        primary = upper?.Text ?? "";
        // The upper slot stays empty on the final odd row; it must not repeat the song title.
        var upperNext = pair.Upper >= 0 && pair.Upper + 1 < shown.Count ? shown[pair.Upper + 1] : null;
        primaryProgress = index < 0 ? 0 : DesktopLyricsLayout.Fill(upper, upperNext, position, duration, settings!.Interpolated, settings.WordHighlight);
        if (secondaryTrack)
        {
            var source = secondarySource;
            secondary = DesktopLyricsLayout.SecondaryText(source, roman, translation);
            var sourceIndex = secondarySourceIndex;
            var sourceNext = sourceIndex >= 0 && sourceIndex + 1 < shown.Count ? shown[sourceIndex + 1] : null;
            secondaryProgress = !settings!.Interpolated ? (secondary.Length == 0 ? 0 : 1) : DesktopLyricsLayout.Fill(source, sourceNext, position, duration, true, false);
            return;
        }
        if (pair.Lower < 0 || pair.Lower >= shown.Count) return;
        secondary = shown[pair.Lower].Text;
        var lowerNext = pair.Lower + 1 < shown.Count ? shown[pair.Lower + 1] : null;
        var lowerActive = pair.Lower == index;
        secondaryProgress = !settings!.Interpolated && lowerActive ? 1 : DesktopLyricsLayout.Fill(shown[pair.Lower], lowerNext, position, duration, settings!.Interpolated, settings.WordHighlight);
    }
    private int IndexOf(LyricLine line)
    {
        for (var i = 0; i < shown.Count; i++)
            if (ReferenceEquals(shown[i], line)) return i;
        return -1;
    }
    private bool HasSecondary() => secondaryRoman || secondaryTranslation;
    private void ReadSecondaryTrack()
    {
        secondaryTranslation = settings?.Translation == true && DesktopLyricsLayout.HasSecondaryTrack(shown, false, true);
        secondaryRoman = !secondaryTranslation && settings?.Romanization == true && DesktopLyricsLayout.HasSecondaryTrack(shown, true, false);
        pairedIndex = int.MinValue;
    }
    private void DrawLine(DrawingContext dc, LineCache.Entry entry, double progress, double x, double y, double available, bool alignLeft, double elapsed, double opacity)
    {
        var length = entry.Vertical ? entry.Height : entry.Width;
        var overflow = length - available;
        var shift = overflow > 1
            ? lyrics?.Document.IsSynced == true
                ? DesktopLyricsLayout.FollowOffset(length, available, progress)
                : DesktopLyricsLayout.MarqueeOffset(overflow, elapsed)
            : 0;
        var origin = overflow > 1 || alignLeft ? 0 : Math.Max(0, (available - length) / 2);
        var clip = entry.Vertical ? new Rect(x - 8, 0, entry.Width + 16, available) : new Rect(0, y - 8, available, entry.Height + 16);
        var enter = Enter(out var fade);
        using (dc.PushOpacity(fade * opacity))
        using (dc.PushClip(clip))
        using (dc.PushTransform(Matrix.CreateTranslation((entry.Vertical ? x : origin + shift) + enter.X, (entry.Vertical ? origin + shift : y) + enter.Y)))
            entry.Draw(dc, progress, settings!.StrokeEnabled, settings.DropShadow);
    }
    private Vector Enter(out double fade)
    {
        fade = 1;
        if (motion?.CanAnimate != true) return default;
        var t = marquee.Elapsed.TotalMilliseconds / 160;
        if (t >= 1) return default;
        var eased = 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
        fade = 0.62 + 0.38 * eased;
        var shift = 3 * (1 - eased);
        return settings?.TextDirection == "vertical" ? new Vector(-shift, 0) : new Vector(0, shift);
    }
    private Paint PaintFor(bool lower)
    {
        var playedTop = themePlayed;
        var playedBottom = themePlayed;
        if (settings!.ColorMode == "custom") playedTop = playedBottom = Parse(settings.Color, Colors.White);
        else if (settings.ColorMode == "gradient")
        {
            playedTop = Parse(settings.GradientStart, Color.Parse("#4F46E5"));
            playedBottom = Parse(settings.GradientEnd, Color.Parse("#EC4899"));
        }
        var unplayedTop = Parse(settings.UnplayedTop, Colors.White);
        var unplayedBottom = Parse(settings.UnplayedBottom, Color.Parse("#CCCCCC"));
        var outlinePlayed = Parse(settings.OutlinePlayed, themeOutline);
        var outlineUnplayed = Parse(settings.OutlineUnplayed, themeOutline);
        if (lower && settings.IndependentRows)
        {
            if (settings.ColorMode == "custom") playedTop = playedBottom = Parse(settings.LowerPlayedTop, playedTop);
            else if (settings.ColorMode == "gradient")
            {
                playedTop = Parse(settings.LowerPlayedTop, playedTop);
                playedBottom = Parse(settings.LowerPlayedBottom, playedBottom);
            }
            unplayedTop = Parse(settings.LowerUnplayedTop, unplayedTop);
            unplayedBottom = Parse(settings.LowerUnplayedBottom, unplayedBottom);
            outlinePlayed = Parse(settings.LowerOutlinePlayed, outlinePlayed);
            outlineUnplayed = Parse(settings.LowerOutlineUnplayed, outlineUnplayed);
        }
        return new(playedTop, playedBottom, unplayedTop, unplayedBottom, outlinePlayed, outlineUnplayed, settings.TextDirection == "vertical");
    }
    private void EnsureShown(LyricsDocument doc)
    {
        if (ReferenceEquals(shownDocument, doc) && shownTitle == lyrics!.Title && shownArtist == lyrics.Artist) return;
        shownDocument = doc; shownTitle = lyrics!.Title; shownArtist = lyrics.Artist;
        shown = DesktopLyricsLayout.WithPlayInfo(doc.Lines, lyrics.Title, lyrics.Artist);
        ReadSecondaryTrack(); cache.Dispose();
    }
    private void ReadTheme()
    {
        var variant = ActualThemeVariant;
        if (variant == themeSeen) return;
        themeSeen = variant;
        themePlayed = this.TryGetResource("EchoHeading", variant, out var found) && found is SolidColorBrush brush ? brush.Color : Colors.White;
        themeOutline = variant == ThemeVariant.Dark ? Color.Parse("#020617") : Color.FromArgb(178, 0x24, 0x27, 0x30);
        cache.Dispose();
    }
    private static Color Parse(string text, Color fallback) => Color.TryParse(text, out var color) ? color : fallback;
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; fontToken++; frames.Dispose(); cache.Dispose(); ClearFont();
        if (lyrics is not null) lyrics.Changed -= Refresh;
        if (settings is not null) settings.PropertyChanged -= PreferencesChanged;
        if (motion is not null) motion.PropertyChanged -= MotionChanged;
        lyrics = null; settings = null; motion = null;
    }
    private readonly record struct Paint(Color PlayedTop, Color PlayedBottom, Color UnplayedTop, Color UnplayedBottom, Color OutlinePlayed, Color OutlineUnplayed, bool Vertical);
}
