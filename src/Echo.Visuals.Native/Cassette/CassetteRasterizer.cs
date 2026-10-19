using System.Globalization;
using System.Text.RegularExpressions;
using Echo.Visuals.Canvas;
using SkiaSharp;

namespace Echo.Visuals.Cassette;

internal readonly record struct CassetteGlyph(int CharIndex, bool Sub, double U0, double V0, double U1, double V1,
    double X, double Y, double Width, double Height);
internal sealed record CassetteRaster(SKBitmap Bitmap, CassetteGlyph[] Glyphs, double Width, double Height) : IDisposable
{ public void Dispose() => Bitmap.Dispose(); }

// A scene owns this bounded font cache. Raster work runs only on a line cache miss.
internal sealed partial class CassetteRasterizer : IDisposable
{
    public const string LyricsFont = "\"Yu Gothic UI\", \"Microsoft YaHei UI\", \"PingFang SC\", \"Hiragino Sans\", \"Noto Sans CJK SC\", \"Malgun Gothic\", system-ui, sans-serif";
    private readonly CanvasFontCache fonts = new();
    private sealed record Pending(string Text, int CharIndex, bool Sub, double Px, double X, double Y, double Width);
    private SKFont Font(double px, int weight, string stack, string text)
        => fonts.Get(FormattableString.Invariant($"{weight} {px}px {stack}"), text);
    private double Measure(string text, double px, int weight, string stack) => Font(px, weight, stack, text).MeasureText(text);
    private static IEnumerable<(string Text, int Index)> Graphemes(string text)
    {
        var iterator = StringInfo.GetTextElementEnumerator(text);
        while (iterator.MoveNext()) yield return ((string)iterator.Current, iterator.ElementIndex);
    }
    private List<string> Wrap(string text, double maxWidth, int maxRows, double px, int weight, string stack)
    {
        var width = Measure(text, px, weight, stack);
        if (width <= maxWidth) return [text];
        var spaced = Whitespace().IsMatch(text.Trim()) && !Cjk().IsMatch(text);
        var units = spaced ? WhitespaceParts().Split(text).Where(t => t.Length > 0) : Graphemes(text).Select(t => t.Text);
        var rows = new List<string>(); var row = "";
        var target = Math.Min(maxWidth, width / Math.Min(maxRows, Math.Ceiling(width / maxWidth)) * 1.08);
        foreach (var unit in units)
        {
            var next = row + unit;
            if (!string.IsNullOrWhiteSpace(row) && Measure(next, px, weight, stack) > target && rows.Count < maxRows - 1)
            { rows.Add(row.TrimEnd()); row = unit.TrimStart(); } else row = next;
        }
        if (!string.IsNullOrWhiteSpace(row)) rows.Add(row.Trim());
        return rows;
    }
    public CassetteRaster? Line(string text, string? translation, double fontPx, double ratio, double maxWidth, string stack, double subScale)
    {
        var main = text.Trim(); main = main[..Math.Min(240, main.Length)];
        var sub = translation?.Trim(); if (sub is not null) sub = sub[..Math.Min(240, sub.Length)];
        var size = fontPx;
        while (size > 16 && Wrap(main, maxWidth, 3, size, 700, stack).Any(row => Measure(row, size, 700, stack) > maxWidth)) size = CassetteLayout.Round(size * .9);
        var mainRows = Wrap(main, maxWidth, 3, size, 700, stack);
        var subSize = Math.Max(12, CassetteLayout.Round(size * .5 * subScale));
        var subRows = !string.IsNullOrEmpty(sub) ? Wrap(sub, maxWidth, 2, subSize, 600, stack) : [];
        var pending = new List<Pending>(160); var lineWidth = 0d;
        var lineGap = size * 1.24; var subGap = subSize * 1.35;
        var totalHeight = mainRows.Count * lineGap + (subRows.Count > 0 ? subRows.Count * subGap + size * .2 : 0);
        var cursorY = -totalHeight / 2; var charBase = 0;
        void Layout(List<string> rows, bool secondary, double px, double gap)
        {
            var weight = secondary ? 600 : 700;
            foreach (var row in rows)
            {
                var rowWidth = Measure(row, px, weight, stack); lineWidth = Math.Max(lineWidth, rowWidth);
                foreach (var (ch, index) in Graphemes(row))
                {
                    if (string.IsNullOrWhiteSpace(ch)) continue;
                    var before = Measure(row[..index], px, weight, stack); var w = Measure(ch, px, weight, stack);
                    var source = secondary ? -1 : main.IndexOf(row, charBase, StringComparison.Ordinal) + index;
                    pending.Add(new(ch, secondary ? -1 : Math.Max(0, source), secondary, px, -rowWidth / 2 + before + w / 2, cursorY + gap / 2, w));
                    if (pending.Count >= 160) return;
                }
                if (!secondary) charBase = Math.Max(charBase, main.IndexOf(row, charBase, StringComparison.Ordinal) + row.Length);
                cursorY += gap;
            }
        }
        Layout(mainRows, false, size, lineGap);
        if (subRows.Count > 0) { cursorY += size * .2; Layout(subRows, true, subSize, subGap); }
        if (pending.Count == 0) return null;
        var pad = (int)Math.Ceiling(size * .34 * ratio); var cellHeight = (int)Math.Ceiling(size * 1.3 * ratio) + pad * 2;
        var cells = new SKRectI[pending.Count]; var x = 0; var y = 0; var rowHeight = 0; var atlasWidth = 0;
        for (var i = 0; i < pending.Count; i++)
        {
            var g = pending[i]; var cw = (int)Math.Ceiling(Math.Max(g.Width, g.Px * .3) * ratio) + pad * 2;
            var ch = g.Sub ? (int)Math.Ceiling(g.Px * 1.3 * ratio) + pad * 2 : cellHeight;
            if (x + cw > 2048) { x = 0; y += rowHeight; rowHeight = 0; }
            cells[i] = SKRectI.Create(x, y, cw, ch); x += cw; rowHeight = Math.Max(rowHeight, ch); atlasWidth = Math.Max(atlasWidth, x);
        }
        var atlasHeight = y + rowHeight;
        if ((long)atlasWidth * atlasHeight > 16 * 1024 * 1024) throw new InvalidDataException("Cassette atlas pixel budget exceeded.");
        var bitmap = new SKBitmap(Math.Max(1, atlasWidth), Math.Max(1, atlasHeight), SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Black);
            using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus, Color = SKColors.Lime };
            var offscreen = bitmap.Width + bitmap.Height + 64;
            for (var i = 0; i < pending.Count; i++)
            {
                var g = pending[i]; var cell = cells[i]; var px = g.Px * ratio;
                using var shadow = SKImageFilter.CreateDropShadow(offscreen, 0, (float)(px * .3 / 2), (float)(px * .3 / 2), SKColors.Lime);
                paint.ImageFilter = shadow;
                canvas.Save(); using var clip = new SKPath(); clip.AddRect(cell); canvas.ClipPath(clip, SKClipOperation.Intersect, true);
                Text(canvas, g.Text, (cell.Left + cell.Right) / 2d - offscreen, (cell.Top + cell.Bottom) / 2d, px, g.Sub ? 600 : 700, stack, paint, true);
                Text(canvas, g.Text, (cell.Left + cell.Right) / 2d - offscreen, (cell.Top + cell.Bottom) / 2d, px, g.Sub ? 600 : 700, stack, paint, true);
                canvas.Restore(); paint.ImageFilter = null;
            }
            paint.Color = SKColors.Red;
            var glyphs = new CassetteGlyph[pending.Count];
            for (var i = 0; i < pending.Count; i++)
            {
                var g = pending[i]; var cell = cells[i];
                Text(canvas, g.Text, (cell.Left + cell.Right) / 2d, (cell.Top + cell.Bottom) / 2d, g.Px * ratio, g.Sub ? 600 : 700, stack, paint, true);
                glyphs[i] = new(g.CharIndex, g.Sub, (double)cell.Left / bitmap.Width, (double)cell.Top / bitmap.Height,
                    (double)cell.Right / bitmap.Width, (double)cell.Bottom / bitmap.Height, g.X, g.Y, cell.Width / ratio, cell.Height / ratio);
            }
            return new(bitmap, glyphs, lineWidth, totalHeight);
        }
        catch { bitmap.Dispose(); throw; }
    }
    private void Text(SKCanvas canvas, string text, double x, double y, double size, int weight, string stack, SKPaint paint, bool center)
    {
        var font = Font(size, weight, stack, text); var width = font.MeasureText(text); var metrics = font.Metrics;
        var fx = (float)x; if (center) fx -= width / 2;
        var fy = (float)y - (metrics.Ascent + metrics.Descent) / 2;
        canvas.DrawText(text, fx, fy, font, paint);
    }
    public void Dispose() => fonts.Dispose();
    [GeneratedRegex(@"\s")] private static partial Regex Whitespace();
    [GeneratedRegex(@"(\s+)")] private static partial Regex WhitespaceParts();
    [GeneratedRegex(@"[\u3040-\u30ff\u3400-\u9fff\uac00-\ud7af]")] private static partial Regex Cjk();
}
