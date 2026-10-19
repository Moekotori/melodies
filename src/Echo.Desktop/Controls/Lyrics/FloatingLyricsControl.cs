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
public sealed class FloatingLyricsControl : Control, IDisposable
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
    private string marqueeKey = "";
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
        ReadTypeface(); cache.Dispose(); Refresh();
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
        ResolveRows(position, duration, out var primary, out var secondary, out var primaryProgress, out var secondaryProgress, out var secondaryIsBody);
        var key = primary + "\n" + secondary;
        if (key != marqueeKey) { marqueeKey = key; marquee.Restart(); }
        var elapsed = lyrics.IsPlaying ? marquee.Elapsed.TotalSeconds : 0;
        var primaryPaint = PaintFor(false);
        var secondaryPaint = PaintFor(true);
        var secondaryPx = secondaryIsBody ? primarySize : secondarySize;
        var primaryLine = cache.Get(primary, medium, primarySize, primaryPaint, vertical);
        var secondaryLine = secondary.Length == 0 ? null : cache.Get(secondary, secondaryIsBody ? medium : regular, secondaryPx, secondaryPaint, vertical);
        var gap = secondaryLine is null && settings.DoubleLine && !HasSecondary() ? secondarySize * DesktopLyricsLayout.LineHeight : 0;
        using (dc.PushOpacity(Math.Clamp(settings.Opacity, .35, 1)))
        {
            if (!vertical)
            {
                var block = primaryLine.Height + (secondaryLine?.Height ?? gap) + (secondaryLine is null ? 0 : DesktopLyricsLayout.LineGap);
                var y = Math.Max(0, (Bounds.Height - block) / 2);
                DrawLine(dc, primaryLine, primaryProgress, 0, y, Bounds.Width, alignLeft, elapsed);
                if (secondaryLine is not null) DrawLine(dc, secondaryLine, secondaryProgress, 0, y + primaryLine.Height + DesktopLyricsLayout.LineGap, Bounds.Width, alignLeft, elapsed);
            }
            else
            {
                var secondaryWidth = secondaryLine?.Width ?? 0;
                var block = primaryLine.Width + (secondaryLine is null ? 0 : secondaryWidth + DesktopLyricsLayout.LineGap);
                var x = Math.Max(0, (Bounds.Width - block) / 2);
                if (secondaryLine is not null) DrawLine(dc, secondaryLine, secondaryProgress, x, 0, Bounds.Height, alignLeft, elapsed);
                DrawLine(dc, primaryLine, primaryProgress, x + (secondaryLine is null ? 0 : secondaryWidth + DesktopLyricsLayout.LineGap), 0, Bounds.Height, alignLeft, elapsed);
            }
        }
    }
    private void ResolveRows(double position, double duration, out string primary, out string secondary, out double primaryProgress, out double secondaryProgress, out bool secondaryIsBody)
    {
        primaryProgress = secondaryProgress = 0; secondary = ""; secondaryIsBody = false;
        if (shown.Count == 0)
        {
            primary = lyrics!.IsLoading ? lyrics.MessageDisplay : DesktopLyricsLayout.PlayInfo(lyrics.Title, lyrics.Artist);
            if (primary.Length == 0) primary = "melodies";
            return;
        }
        var synced = lyrics!.Document.IsSynced;
        var index = synced ? DesktopLyricsLayout.SyncedIndex(shown, position) : DesktopLyricsLayout.PlainIndex(shown.Count, position, duration);
        var roman = settings!.Romanization;
        var translation = settings.Translation;
        var secondaryTrack = DesktopLyricsLayout.HasSecondaryTrack(shown, roman, translation);
        var pair = DesktopLyricsLayout.Pair(Math.Max(index, 0), shown.Count, settings.DoubleLine, secondaryTrack);
        if (index < 0) pair = new(0, settings.DoubleLine && !secondaryTrack && shown.Count > 1 ? 1 : -1);
        var upper = pair.Upper >= 0 && pair.Upper < shown.Count ? shown[pair.Upper] : null;
        primary = upper?.Text ?? "";
        if (primary.Length == 0) primary = DesktopLyricsLayout.PlayInfo(lyrics.Title, lyrics.Artist);
        if (primary.Length == 0) primary = "melodies";
        var upperNext = pair.Upper >= 0 && pair.Upper + 1 < shown.Count ? shown[pair.Upper + 1] : null;
        primaryProgress = index < 0 ? 0 : DesktopLyricsLayout.Fill(upper, upperNext, position, duration, settings.Interpolated);
        if (secondaryTrack)
        {
            var source = DesktopLyricsLayout.SecondarySource(shown, index, roman, translation);
            secondary = DesktopLyricsLayout.SecondaryText(source, roman, translation);
            var sourceIndex = source is null ? -1 : IndexOf(source);
            var sourceNext = sourceIndex >= 0 && sourceIndex + 1 < shown.Count ? shown[sourceIndex + 1] : null;
            secondaryProgress = !settings.Interpolated ? (secondary.Length == 0 ? 0 : 1) : DesktopLyricsLayout.Fill(source, sourceNext, position, duration, true);
            return;
        }
        if (pair.Lower < 0 || pair.Lower >= shown.Count) return;
        secondary = shown[pair.Lower].Text;
        secondaryIsBody = true;
        var lowerNext = pair.Lower + 1 < shown.Count ? shown[pair.Lower + 1] : null;
        var lowerActive = pair.Lower == index;
        secondaryProgress = !settings.Interpolated && lowerActive ? 1 : DesktopLyricsLayout.Fill(shown[pair.Lower], lowerNext, position, duration, settings.Interpolated);
    }
    private int IndexOf(LyricLine line)
    {
        for (var i = 0; i < shown.Count; i++)
            if (ReferenceEquals(shown[i], line)) return i;
        return -1;
    }
    private bool HasSecondary() => settings is not null && DesktopLyricsLayout.HasSecondaryTrack(shown, settings.Romanization, settings.Translation);
    private void DrawLine(DrawingContext dc, LineCache.Entry entry, double progress, double x, double y, double available, bool alignLeft, double elapsed)
    {
        var length = entry.Vertical ? entry.Height : entry.Width;
        var overflow = length - available;
        var shift = overflow > 1 ? DesktopLyricsLayout.MarqueeOffset(overflow, elapsed) : 0;
        var origin = overflow > 1 || alignLeft ? 0 : Math.Max(0, (available - length) / 2);
        var clip = entry.Vertical ? new Rect(x - 8, 0, entry.Width + 16, available) : new Rect(0, y - 8, available, entry.Height + 16);
        var enter = Enter(out var fade);
        using (dc.PushOpacity(fade))
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
        cache.Dispose();
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
    private void ReadTypeface()
    {
        try
        {
            var family = customFont ? medium.FontFamily : new FontFamily(settings?.FontFamily is { Length: > 0 and <= 256 } s ? s : DesktopLyricsFont.Default);
            medium = new(family, FontStyle.Normal, FontWeight.Medium);
            regular = new(family, FontStyle.Normal, FontWeight.Normal);
        }
        catch (Exception error) when (error is ArgumentException or UriFormatException)
        {
            medium = new(new FontFamily(DesktopLyricsFont.Default), FontStyle.Normal, FontWeight.Medium);
            regular = new(medium.FontFamily, FontStyle.Normal, FontWeight.Normal);
        }
    }
    private void QueueFont()
    {
        var path = settings?.FontFilePath ?? "";
        var token = ++fontToken;
        if (path.Length == 0) { ClearFont(); ReadTypeface(); cache.Dispose(); return; }
        Task.Run(() =>
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length is 0 or > 32 * 1024 * 1024) return null;
                return File.ReadAllBytes(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        }).ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            if (disposed || token != fontToken) return;
            InstallFont(task.Status == TaskStatus.RanToCompletion ? task.Result : null);
        }));
    }
    private void InstallFont(byte[]? bytes)
    {
        ClearFont();
        if (bytes is null) { ReadTypeface(); cache.Dispose(); Refresh(); return; }
        var bag = new FontBag();
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            if (!bag.TryAddGlyphTypeface(stream, out var face) || string.IsNullOrWhiteSpace(face.FamilyName)) ((IDisposable)bag).Dispose();
            else
            {
                FontManager.Current.AddFontCollection(bag);
                fonts = bag; customFont = true;
                var family = new FontFamily(bag.Key.AbsoluteUri + "#" + face.FamilyName);
                medium = new(family, FontStyle.Normal, FontWeight.Medium);
                regular = new(family, FontStyle.Normal, FontWeight.Normal);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or UriFormatException)
        { ((IDisposable)bag).Dispose(); customFont = false; }
        cache.Dispose(); Refresh();
    }
    private void ClearFont()
    {
        customFont = false;
        if (fonts is null) return;
        var bag = fonts; fonts = null;
        try { FontManager.Current.RemoveFontCollection(bag.Key); } catch (InvalidOperationException) { }
        ((IDisposable)bag).Dispose();
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
    private sealed class FontBag : FontCollectionBase { public override Uri Key { get; } = new("fonts:echo-desktop-lyrics"); }
    private sealed class LineCache : IDisposable
    {
        private readonly Dictionary<StyleKey, Entry> entries = [];
        public int Count { get; private set; }
        public Entry Get(string text, Typeface face, double size, Paint paint, bool vertical)
        {
            if (text.Length > (vertical ? 48 : 400)) text = text[..(vertical ? 48 : 400)];
            var key = new StyleKey(text, (int)Math.Round(size * 100), face.Weight, paint, vertical);
            if (entries.TryGetValue(key, out var entry)) return entry;
            entry = new(text, face, size, paint, vertical);
            while (entries.Count > 0 && (entries.Count >= 6 || Count + entry.Layouts > 80))
            {
                var first = entries.First(); first.Value.Dispose(); Count -= first.Value.Layouts; entries.Remove(first.Key);
            }
            entries[key] = entry; Count += entry.Layouts; return entry;
        }
        public void Dispose()
        {
            foreach (var entry in entries.Values) entry.Dispose();
            entries.Clear(); Count = 0;
        }
        private readonly record struct StyleKey(string Text, int Size, FontWeight Weight, Paint Paint, bool Vertical);
        internal sealed class Entry : IDisposable
        {
            private readonly TextLayout? stroke;
            private readonly TextLayout? playedStroke;
            private readonly TextLayout? rest;
            private readonly TextLayout? played;
            private readonly TextLayout? shadow;
            private readonly DesktopLyricsColumn? column;
            public bool Vertical { get; }
            public double Width { get; }
            public double Height { get; }
            public int Layouts { get; }
            public Entry(string text, Typeface face, double size, Paint paint, bool vertical)
            {
                Vertical = vertical;
                if (vertical)
                {
                    column = new(text, face, size, paint.PlayedTop, paint.PlayedBottom, paint.UnplayedTop, paint.UnplayedBottom, paint.OutlinePlayed, paint.OutlineUnplayed);
                    Width = column.Width;
                    Height = column.Height;
                    Layouts = column.Layouts;
                    return;
                }
                stroke = Shape(text, face, size, Brush(paint.OutlineUnplayed, paint.OutlineUnplayed), 1, 100000, TextAlignment.Left);
                playedStroke = Shape(text, face, size, Brush(paint.OutlinePlayed, paint.OutlinePlayed), 1, 100000, TextAlignment.Left);
                rest = Shape(text, face, size, Brush(paint.UnplayedTop, paint.UnplayedBottom), 1, 100000, TextAlignment.Left);
                played = Shape(text, face, size, Brush(paint.PlayedTop, paint.PlayedBottom), 1, 100000, TextAlignment.Left);
                shadow = Shape(text, face, size, new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), 1, 100000, TextAlignment.Left);
                Width = rest.Width;
                Height = Math.Max(size * DesktopLyricsLayout.LineHeight, rest.Height);
                Layouts = 5;
            }
            public void Draw(DrawingContext dc, double progress, bool strokeOn, bool shadowOn)
            {
                if (column is not null)
                {
                    column.Draw(dc, progress, strokeOn, shadowOn);
                    return;
                }
                if (rest is null || played is null) return;
                if (shadowOn && shadow is not null) shadow.Draw(dc, new(0, 1));
                if (strokeOn && stroke is not null)
                    foreach (var offset in Outline) stroke.Draw(dc, new(offset.X, offset.Y));
                rest.Draw(dc, default);
                var amount = Math.Clamp(progress, 0, 1) * Math.Max(1, Width);
                using (dc.PushClip(new Rect(0, -4, amount, Height + 8)))
                {
                    if (strokeOn && playedStroke is not null)
                        foreach (var offset in Outline) playedStroke.Draw(dc, new(offset.X, offset.Y));
                    played.Draw(dc, default);
                }
            }
            private static TextLayout Shape(string text, Typeface face, double size, IBrush brush, int lines, double maxWidth, TextAlignment align) => new(text, face, size, brush,
                textAlignment: align, textWrapping: TextWrapping.NoWrap, maxWidth: maxWidth, maxLines: lines, lineHeight: size * DesktopLyricsLayout.LineHeight);
            private static IBrush Brush(Color top, Color bottom) => top == bottom ? new SolidColorBrush(top) : new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new(top, 0), new(bottom, 1) }
            };
            public void Dispose()
            {
                column?.Dispose();
                stroke?.Dispose(); playedStroke?.Dispose(); rest?.Dispose(); played?.Dispose(); shadow?.Dispose();
            }
        }
    }
}
