using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.Floating;

namespace Echo.Desktop.Controls.Lyrics;

public sealed partial class FloatingLyricsControl
{
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
