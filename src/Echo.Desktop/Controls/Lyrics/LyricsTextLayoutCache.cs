using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Echo.Contracts.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// Only nearby lines are shaped. Cache is capped at 18 entries and ~4 MiB of source/glyph estimates.
internal sealed class LyricsTextLayoutCache : IDisposable
{
    private readonly Dictionary<Key, Entry> cache = [];
    private long bytes;
    public int Count => cache.Count;
    public Entry Get(LyricLine line, Typeface font, double size, double secondarySize, double width,
        TextAlignment alignment, IBrush ink, IBrush accent, bool translation, bool romanization, double lineHeight = 1.18, double letterSpacing = 0)
    {
        var key = new Key(line, font, size, secondarySize, width, alignment, ink, accent, translation, romanization, lineHeight, letterSpacing);
        if (cache.TryGetValue(key, out var entry)) return entry;
        entry = new(line, font, size, secondarySize, width, alignment, ink, accent, translation, romanization, lineHeight, letterSpacing);
        while (cache.Count > 0 && (cache.Count >= 18 || bytes + entry.Bytes > 4 * 1024 * 1024))
        { var first = cache.First(); first.Value.Dispose(); bytes -= first.Value.Bytes; cache.Remove(first.Key); }
        cache[key] = entry; bytes += entry.Bytes;
        return entry;
    }
    public void Dispose() { foreach (var entry in cache.Values) entry.Dispose(); cache.Clear(); bytes = 0; }
    private readonly record struct Key(LyricLine Line, Typeface Font, double Size, double Secondary, double Width,
        TextAlignment Alignment, IBrush Ink, IBrush Accent, bool Translation, bool Romanization, double LineHeight, double LetterSpacing);
    internal sealed class Entry : IDisposable
    {
        public TextLayout Primary { get; }
        public TextLayout Highlight { get; }
        public TextLayout Tinted { get; }
        private readonly SolidColorBrush tint = new();
        public TextLayout? Secondary { get; }
        public TextLayout? Pronunciation { get; }
        public TextLayout? SecondaryHighlight { get; }
        public TextLayout? PronunciationHighlight { get; }
        public IReadOnlyList<WordClip> Words { get; }
        public double Height { get; }
        public long Bytes { get; }
        public double FontSize { get; }
        public IBrush Accent { get; }
        public IBrush Ink { get; }
        public LyricLine Line { get; }
        public bool Estimated { get; }
        public Entry(LyricLine line, Typeface font, double size, double secondarySize, double width,
            TextAlignment alignment, IBrush ink, IBrush accent, bool translation, bool romanization, double lineHeight, double letterSpacing)
        {
            FontSize = size; Accent = accent; Ink = ink; Line = line; Estimated = line.EstimatedTiming;
            TextLayout Shape(string text, double px, IBrush brush) => new(text, font, px, brush,
                textAlignment: alignment, textWrapping: TextWrapping.Wrap, maxWidth: Math.Max(32, width), maxLines: 5,
                textTrimming: TextTrimming.CharacterEllipsis, lineHeight: px * (px == size ? lineHeight : 1.4), letterSpacing: px == size ? letterSpacing : 0);
            Primary = Shape(line.Text.Length == 0 ? "♪" : line.Text, size, ink);
            Highlight = Shape(line.Text.Length == 0 ? "♪" : line.Text, size, accent);
            Tinted = Shape(line.Text.Length == 0 ? "♪" : line.Text, size, tint);
            if (translation && !string.IsNullOrWhiteSpace(line.Translation))
            { Secondary = Shape(line.Translation, secondarySize, ink); SecondaryHighlight = Shape(line.Translation, secondarySize, accent); }
            if (romanization && !string.IsNullOrWhiteSpace(line.Romanization))
            { Pronunciation = Shape(line.Romanization, secondarySize, ink); PronunciationHighlight = Shape(line.Romanization, secondarySize, accent); }
            Height = Primary.Height + (Secondary?.Height + 10 ?? 0) + (Pronunciation?.Height + 8 ?? 0);
            Bytes = (line.Text.Length + (line.Translation?.Length ?? 0) + (line.Romanization?.Length ?? 0)) * 288L + 3072;
            var clips = new List<WordClip>(); var offset = 0;
            foreach (var word in line.Words.Take(1024))
            {
                var found = line.Text.IndexOf(word.Text, offset, StringComparison.Ordinal);
                if (found < 0 || word.Text.Length == 0) continue;
                double y = 0; var fragments = new List<(Rect Rect, FlowDirection Direction)>();
                foreach (var row in Primary.TextLines)
                {
                    var start = Math.Max(row.FirstTextSourceIndex, found); var end = Math.Min(row.FirstTextSourceIndex + row.Length, found + word.Text.Length);
                    if (end > start)
                        foreach (var bounds in row.GetTextBounds(start, end - start))
                            fragments.Add((bounds.Rectangle.Translate(new Vector(0, y)), bounds.FlowDirection));
                    y += row.Height;
                }
                var totalWidth = fragments.Sum(f => f.Rect.Width); double prefix = 0;
                foreach (var fragment in fragments)
                { clips.Add(new(word, fragment.Rect, prefix, totalWidth, fragment.Direction)); prefix += fragment.Rect.Width; }
                offset = found + word.Text.Length;
            }
            Words = clips;
        }
        public void DrawTinted(DrawingContext dc, Point point, double progress, double upcoming = 1)
        {
            var body = ((ISolidColorBrush)Ink).Color; var fill = ((ISolidColorBrush)Accent).Color;
            var a = body.A / 255d * upcoming * (1 - progress); var b = fill.A / 255d * progress;
            var alpha = a + b;
            tint.Color = alpha <= 0 ? Colors.Transparent : Color.FromArgb((byte)Math.Round(alpha * 255),
                (byte)Math.Round((body.R * a + fill.R * b) / alpha), (byte)Math.Round((body.G * a + fill.G * b) / alpha),
                (byte)Math.Round((body.B * a + fill.B * b) / alpha));
            Tinted.Draw(dc, point);
        }
        public void Dispose() { Primary.Dispose(); Highlight.Dispose(); Tinted.Dispose(); Secondary?.Dispose(); Pronunciation?.Dispose(); SecondaryHighlight?.Dispose(); PronunciationHighlight?.Dispose(); }
    }
    internal sealed record WordClip(LyricWord Word, Rect Bounds, double PrefixWidth, double TotalWidth, FlowDirection Direction);
}
