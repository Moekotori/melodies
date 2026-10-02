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
        TextAlignment alignment, IBrush ink, IBrush accent, bool translation, bool romanization)
    {
        var key = new Key(line, font, size, secondarySize, width, alignment, ink, accent, translation, romanization);
        if (cache.TryGetValue(key, out var entry)) return entry;
        entry = new(line, font, size, secondarySize, width, alignment, ink, accent, translation, romanization);
        while (cache.Count > 0 && (cache.Count >= 18 || bytes + entry.Bytes > 4 * 1024 * 1024))
        { var first = cache.First(); first.Value.Dispose(); bytes -= first.Value.Bytes; cache.Remove(first.Key); }
        cache[key] = entry; bytes += entry.Bytes;
        return entry;
    }
    public void Dispose() { foreach (var entry in cache.Values) entry.Dispose(); cache.Clear(); bytes = 0; }
    private sealed record Key(LyricLine Line, Typeface Font, double Size, double Secondary, double Width,
        TextAlignment Alignment, IBrush Ink, IBrush Accent, bool Translation, bool Romanization);
    internal sealed class Entry : IDisposable
    {
        public TextLayout Primary { get; }
        public TextLayout Highlight { get; }
        public TextLayout? Secondary { get; }
        public TextLayout? Pronunciation { get; }
        public IReadOnlyList<WordClip> Words { get; }
        public double Height { get; }
        public long Bytes { get; }
        public Entry(LyricLine line, Typeface font, double size, double secondarySize, double width,
            TextAlignment alignment, IBrush ink, IBrush accent, bool translation, bool romanization)
        {
            TextLayout Shape(string text, double px, IBrush brush) => new(text, font, px, brush,
                textAlignment: alignment, textWrapping: TextWrapping.Wrap, maxWidth: Math.Max(32, width), maxLines: 5,
                textTrimming: TextTrimming.CharacterEllipsis);
            Primary = Shape(line.Text.Length == 0 ? "♪" : line.Text, size, ink);
            Highlight = Shape(line.Text.Length == 0 ? "♪" : line.Text, size, accent);
            if (translation && !string.IsNullOrWhiteSpace(line.Translation)) Secondary = Shape(line.Translation, secondarySize, ink);
            if (romanization && !string.IsNullOrWhiteSpace(line.Romanization)) Pronunciation = Shape(line.Romanization, secondarySize, ink);
            Height = Primary.Height + (Secondary?.Height + 10 ?? 0) + (Pronunciation?.Height + 8 ?? 0);
            Bytes = (line.Text.Length + (line.Translation?.Length ?? 0) + (line.Romanization?.Length ?? 0)) * 192L + 2048;
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
        public void Dispose() { Primary.Dispose(); Highlight.Dispose(); Secondary?.Dispose(); Pronunciation?.Dispose(); }
    }
    internal sealed record WordClip(LyricWord Word, Rect Bounds, double PrefixWidth, double TotalWidth, FlowDirection Direction);
}
