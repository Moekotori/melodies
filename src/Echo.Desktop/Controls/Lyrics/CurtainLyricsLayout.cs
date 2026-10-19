using Avalonia;
using Avalonia.Media;
using Echo.Contracts.Lyrics;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// lyrics-curtain.css: the active serif column hangs at 58%; past lines move right.
internal sealed class CurtainLyricsLayout : IDisposable
{
    private readonly Dictionary<Key, Column> columns = [];
    private readonly BlurEffect glow = new() { Radius = 10 };
    private readonly List<(int Index, Column Column)> visible = [];
    private (LyricsDocument Document, int Index, double Size, double Height, Typeface Face, IBrush Ink, IBrush Accent, bool Context, bool Translation, bool Romanization)? prepared;
    public void Draw(DrawingContext dc, LyricsDocument document, int index, double position, Rect viewport,
        LyricsAppearance appearance, IBrush ink, IBrush accent, Action<int, Rect> hit)
    {
        var size = Math.Clamp(viewport.Width * .027, 24, 44) * appearance.FontSize / 40;
        var spacing = Math.Clamp(viewport.Width * .11, 92, 176);
        var face = new Typeface(appearance.Font == "跟随样式" ? LyricsReadingPresentation.Serif : appearance.Font, weight: (FontWeight)600);
        var state = (document, index, size, viewport.Height * .66, face, ink, accent, appearance.Context, appearance.Translation, appearance.Romanization);
        if (prepared != state)
        {
            prepared = state; visible.Clear();
            var count = appearance.Context ? 3 : 0;
            var live = new HashSet<Key>();
            for (var i = Math.Max(0, index - count); i <= Math.Min(document.Lines.Count - 1, index + count); i++)
            {
                var key = new Key(document.Lines[i], size, viewport.Height * .66, face, ink, accent, appearance.Translation && (i == index || !appearance.CurrentTranslationOnly),
                    appearance.Romanization && i == index);
                live.Add(key);
                if (!columns.TryGetValue(key, out var column)) columns[key] = column = new(key);
                visible.Add((i, column));
            }
            foreach (var stale in columns.Keys.Where(k => !live.Contains(k)).ToArray())
            { columns[stale].Dispose(); columns.Remove(stale); }
        }
        foreach (var (i, column) in visible)
        {
            var focus = i == index; var line = document.Lines[i];
            var distance = i - index;
            var x = viewport.X + viewport.Width * .58 - distance * spacing;
            var y = viewport.Y + viewport.Height * .15;
            var scale = focus ? 1 : .8;
            var opacity = focus ? 1 : LyricsReadingPresentation.Opacity(distance, appearance.ContextOpacityPercent);
            using (dc.PushOpacity(opacity))
            using (dc.PushTransform(Matrix.CreateScale(scale, scale)
                * Matrix.CreateTranslation(x - column.Width * scale / 2, y)))
            using (dc.PushClip(new Rect(-column.Width, -50, column.Width * 2 + 16, viewport.Height * .66 + 50)))
            {
                var progress = focus && appearance.WordHighlight ? Progress(line, position) : 0;
                using var group = dc.PushTransform(Matrix.CreateTranslation(column.Width - column.Primary.Width, 0));
                column.Primary.DrawWrapped(dc, progress, column.Height);
                var sx = -12d - (column.Primary.WrappedWidth(column.Height) - column.Primary.Width);
                if (column.Pronunciation is { } pronunciation)
                {
                    sx -= pronunciation.WrappedWidth(column.Height);
                    using (dc.PushOpacity(.42))
                    using (dc.PushTransform(Matrix.CreateTranslation(sx, 0))) pronunciation.DrawWrapped(dc, 0, column.Height);
                    sx -= 12;
                }
                if (column.Translation is { } translation)
                    using (dc.PushOpacity(.58))
                    using (dc.PushTransform(Matrix.CreateTranslation(sx - translation.WrappedWidth(column.Height), 0))) translation.DrawWrapped(dc, 0, column.Height);
                if (focus)
                {
                    var dot = new Point(column.Primary.Width - column.Width / 2, -18.5);
                    using (dc.PushOpacity(.55))
                    using (dc.PushEffect(glow, new Rect(dot.X - 16, dot.Y - 16, 32, 32))) dc.DrawEllipse(accent, null, dot, 5.5, 5.5);
                    dc.DrawEllipse(accent, null, dot, 3.5, 3.5);
                    dc.DrawLine(new Pen(accent, 1), new(dot.X, -48), new(dot.X, -28));
                }
            }
            hit(i, new Rect(x - spacing * .45, y, spacing * .9, viewport.Height * .66));
        }
    }

    private static double Progress(LyricLine line, double position)
    {
        if (line.Words.Count == 0) return position >= line.StartMs ? 1 : 0;
        double filled = 0, total = 0;
        foreach (var word in line.Words)
        {
            var length = word.Text.EnumerateRunes().Count();
            total += length;
            filled += length * Math.Clamp((position - word.StartMs) / Math.Max(1, word.EndMs - word.StartMs), 0, 1);
        }
        return total > 0 ? filled / total : 0;
    }
    public void Dispose() { foreach (var column in columns.Values) column.Dispose(); columns.Clear(); visible.Clear(); prepared = null; }
    private sealed record Key(LyricLine Line, double Size, double Height, Typeface Font, IBrush Ink, IBrush Accent, bool Translation, bool Romanization);
    private sealed class Column : IDisposable
    {
        public DesktopLyricsColumn Primary { get; }
        public DesktopLyricsColumn? Translation { get; }
        public DesktopLyricsColumn? Pronunciation { get; }
        public double Height { get; }
        public double Width => Primary.WrappedWidth(Height) + (Translation?.WrappedWidth(Height) + 12 ?? 0) + (Pronunciation?.WrappedWidth(Height) + 12 ?? 0);
        public Column(Key key)
        {
            Height = key.Height;
            var ink = ((ISolidColorBrush)key.Ink).Color; var accent = ((ISolidColorBrush)key.Accent).Color;
            DesktopLyricsColumn Shape(string text, double px, Typeface font) => new(BoundedText(text), font, px,
                accent, accent, ink, ink, Colors.Transparent, Colors.Transparent, font == key.Font ? 1.04 : 1.12, 1.5, font == key.Font ? .04 : .12);
            Primary = Shape(key.Line.Text, key.Size, key.Font);
            var sans = new Typeface(LyricsReadingPresentation.Sans);
            if (key.Translation && !string.IsNullOrWhiteSpace(key.Line.Translation)) Translation = Shape(key.Line.Translation, key.Size * .47, sans);
            if (key.Romanization && !string.IsNullOrWhiteSpace(key.Line.Romanization)) Pronunciation = Shape(key.Line.Romanization, key.Size * .4, sans);
        }
        // Same 192-grapheme presentation budget as the existing Afterglow renderer.
        private static string BoundedText(string text)
        {
            var elements = System.Globalization.StringInfo.GetTextElementEnumerator(text);
            var count = 0;
            while (elements.MoveNext())
                if (count++ == 192) return text[..elements.ElementIndex];
            return text;
        }
        public void Dispose() { Primary.Dispose(); Translation?.Dispose(); Pronunciation?.Dispose(); }
    }
}
