using System.Text;
using SkiaSharp;

namespace Echo.Visuals.Cassette;

internal sealed record CassetteTitle(SKBitmap Ink, SKBitmap Neon, double Width, double Height, int Length) : IDisposable
{ public void Dispose() { Ink.Dispose(); Neon.Dispose(); } }

internal sealed partial class CassetteRasterizer
{
    public CassetteTitle? Title(string title, double rasterPx)
    {
        const string stack = "\"MS Gothic\", \"Osaka-Mono\", \"Noto Sans Mono CJK SC\", \"Courier New\", monospace";
        var clean = WhitespaceParts().Replace(title, " ").Trim();
        if (clean.Length == 0) return null;
        var px = Math.Max(16, Math.Min(72, CassetteLayout.Round(rasterPx))); var maxPx = 640d / 30 * px;
        var text = clean;
        if (Measure(text, px, 700, stack) > maxPx)
        {
            var chars = clean.EnumerateRunes().Select(r => r.ToString()).ToList();
            while (chars.Count > 1 && Measure(string.Concat(chars) + "…", px, 700, stack) > maxPx) chars.RemoveAt(chars.Count - 1);
            text = string.Concat(chars) + "…";
        }
        var pad = (int)Math.Ceiling(px * .18); var width = (int)Math.Ceiling(Measure(text, px, 700, stack)) + pad * 2;
        var height = (int)Math.Ceiling(px * 1.3) + pad * 2;
        SKBitmap Draw(bool neon)
        {
            var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            try
            {
                using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { IsAntialias = true };
                if (neon)
                {
                    paint.Style = SKPaintStyle.Stroke; paint.StrokeJoin = SKStrokeJoin.Round;
                    paint.StrokeWidth = (float)Math.Max(2, px * .14); paint.Color = new(0x16, 0x2d, 0x8d);
                    Text(canvas, text, pad, height / 2d, px, 700, stack, paint, false);
                }
                paint.Style = SKPaintStyle.Fill; paint.Color = neon ? new(0xf7, 0x7d, 0xff) : new(0x2e, 0x11, 0x3f);
                Text(canvas, text, pad, height / 2d, px, 700, stack, paint, false);
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
        var ink = Draw(false);
        try { return new(ink, Draw(true), width * (30 / px), height * (30 / px), text.EnumerateRunes().Count()); }
        catch { ink.Dispose(); throw; }
    }
}
