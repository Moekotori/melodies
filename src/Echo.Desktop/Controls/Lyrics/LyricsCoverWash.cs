using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Echo.Desktop.Theming;
using SkiaSharp;

namespace Echo.Desktop.Controls.Lyrics;

// One graded cover. Blur and color filters run when the artwork or stage shape changes, never per lyric frame.
internal sealed class LyricsCoverWash : IDisposable
{
    private Bitmap? source;
    private int width, height, sigmaMilli, brightMilli, saturateMilli, contrastMilli;
    private RenderTargetBitmap? low;
    private SKBitmap? scratch;
    private WriteableBitmap? soft;
    public Bitmap? Image => soft;

    public void Prepare(Bitmap artwork, double stageWidth, double stageHeight, double blur, double brightness, double saturate = 1, double contrast = 1)
    {
        if (stageWidth < 8 || stageHeight < 8) return;
        var longEdge = blur <= 0.5 ? 1600 : blur <= 16 ? 1100 : blur <= 28 ? 640 : 320;
        var aspect = stageWidth / stageHeight;
        var w = aspect >= 1 ? longEdge : Math.Max(8, (int)Math.Round(longEdge * aspect));
        var h = aspect >= 1 ? Math.Max(8, (int)Math.Round(longEdge / aspect)) : longEdge;
        w = Math.Max(8, w / 4 * 4);
        h = Math.Max(8, h / 4 * 4);
        var sigma = blur <= 0.5 ? 0 : (int)Math.Round(Math.Clamp(blur * w / stageWidth, 0.4, 48) * 100);
        var bright = (int)Math.Round(Math.Clamp(brightness, 0.05, 1.6) * 1000);
        var sat = (int)Math.Round(Math.Clamp(saturate, 0, 2) * 1000);
        var con = (int)Math.Round(Math.Clamp(contrast, 0, 2) * 1000);
        if (ReferenceEquals(source, artwork) && width == w && height == h && sigmaMilli == sigma
            && brightMilli == bright && saturateMilli == sat && contrastMilli == con && soft is not null) return;
        source = artwork;
        width = w;
        height = h;
        sigmaMilli = sigma;
        brightMilli = bright;
        saturateMilli = sat;
        contrastMilli = con;
        try { Render(artwork, w, h, sigma / 100f, bright / 1000d, sat / 1000d, con / 1000d); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException) { Release(); }
    }

    private void Render(Bitmap artwork, int w, int h, float sigma, double brightness, double saturate, double contrast)
    {
        if (low is null || low.PixelSize.Width != w || low.PixelSize.Height != h)
        { low?.Dispose(); low = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96)); }
        using (var dc = low.CreateDrawingContext())
        {
            using (dc.PushRenderOptions(new RenderOptions { BitmapBlendingMode = BitmapBlendingMode.Source }))
                dc.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
            LyricsStagePainter.Image(dc, artwork, new Rect(0, 0, w, h));
        }
        if (scratch is null || scratch.Width != w || scratch.Height != h)
        { scratch?.Dispose(); scratch = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul)); }
        low.CopyPixels(new WallpaperGrade.Framebuffer(scratch));
        SKBitmap graded = scratch;
        SKBitmap? blurred = null;
        if (sigma > 0.2f)
        {
            blurred = new SKBitmap(scratch.Info);
            WallpaperGrade.BlurInto(scratch, blurred, sigma);
            graded = blurred;
        }
        Grade(graded, brightness, saturate, contrast);
        if (soft is null || soft.PixelSize.Width != w || soft.PixelSize.Height != h)
        { soft?.Dispose(); soft = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul); }
        using var locked = soft.Lock();
        WallpaperGrade.Copy(graded.GetPixels(), graded.RowBytes, locked.Address, locked.RowBytes, w, h);
        blurred?.Dispose();
    }

    private static void Grade(SKBitmap bitmap, double brightness, double saturate, double contrast)
    {
        var bright = (float)brightness;
        var sat = (float)saturate;
        var con = (float)contrast;
        if (Math.Abs(bright - 1f) <= 0.004f && Math.Abs(sat - 1f) <= 0.004f && Math.Abs(con - 1f) <= 0.004f) return;
        var pixels = bitmap.GetPixelSpan();
        var rowBytes = bitmap.Width * 4;
        for (var y = 0; y < bitmap.Height; y++)
        {
            var row = pixels.Slice(y * bitmap.RowBytes, rowBytes);
            for (var i = 0; i + 3 < row.Length; i += 4)
            {
                float b = row[i] * bright, g = row[i + 1] * bright, r = row[i + 2] * bright;
                if (Math.Abs(sat - 1f) > 0.004f)
                {
                    var luma = 0.0722f * b + 0.7152f * g + 0.2126f * r;
                    b = luma + (b - luma) * sat;
                    g = luma + (g - luma) * sat;
                    r = luma + (r - luma) * sat;
                }
                if (Math.Abs(con - 1f) > 0.004f)
                {
                    b = (b - 128f) * con + 128f;
                    g = (g - 128f) * con + 128f;
                    r = (r - 128f) * con + 128f;
                }
                row[i] = Byte(b);
                row[i + 1] = Byte(g);
                row[i + 2] = Byte(r);
            }
        }
    }

    private static byte Byte(float value) => value >= 255f ? (byte)255 : value <= 0f ? (byte)0 : (byte)(value + 0.5f);

    public void Dispose() => Release();

    private void Release()
    {
        low?.Dispose();
        scratch?.Dispose();
        soft?.Dispose();
        low = null;
        scratch = null;
        soft = null;
        source = null;
    }
}
