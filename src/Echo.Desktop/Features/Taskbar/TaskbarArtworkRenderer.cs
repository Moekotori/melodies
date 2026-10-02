using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Echo.Desktop.Features.Taskbar;

// Small, bounded raster surfaces shared by the native DWM path and preview verification.
public static class TaskbarArtworkRenderer
{
    public static byte[] Cover(Bitmap? image, bool dark, bool showInformation, string title, string artist)
    {
        using var output = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Transparent);
        var edge = showInformation ? 208 : 256;
        var rect = SKRect.Create((256 - edge) / 2f, 0, edge, edge);
        if (image is not null && image.PixelSize.Width <= 512 && image.PixelSize.Height <= 512)
        {
            using var source = new SKBitmap(new SKImageInfo(image.PixelSize.Width, image.PixelSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var frame = new Pixels(source);
            image.CopyPixels(frame);
            var side = Math.Min(source.Width, source.Height);
            var crop = SKRect.Create((source.Width - side) / 2f, (source.Height - side) / 2f, side, side);
            canvas.DrawBitmap(source, crop, rect);
        }
        else
        {
            using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse(dark ? "#292636" : "#EEEAF6") };
            canvas.DrawRect(rect, paint);
            paint.Color = SKColor.Parse(dark ? "#BDB1E8" : "#76619E");
            var center = rect.MidX;
            paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 6; paint.StrokeCap = SKStrokeCap.Round;
            canvas.DrawLine(center + 16, rect.MidY - 30, center + 16, rect.MidY + 22, paint);
            canvas.DrawLine(center + 16, rect.MidY - 30, center + 38, rect.MidY - 22, paint);
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawOval(center + 4, rect.MidY + 24, 15, 11, paint);
        }
        if (showInformation)
        {
            using var typeface = SKTypeface.FromFamilyName("Microsoft YaHei");
            using var font = new SKFont(typeface, 14);
            using var paint = new SKPaint { IsAntialias = true, Color = dark ? SKColors.White : SKColor.Parse("#252A3E") };
            DrawCentered(canvas, title, 222, font, paint);
            font.Size = 11; paint.Color = dark ? SKColor.Parse("#CBC6DA") : SKColor.Parse("#676778");
            DrawCentered(canvas, artist, 243, font, paint);
        }
        return Copy(output);
    }
    private static void DrawCentered(SKCanvas canvas, string value, float baseline, SKFont font, SKPaint paint)
    {
        var text = value.Length > 100 ? value[..100] : value;
        var truncated = false;
        while (text.Length > 0 && font.MeasureText(text + (truncated ? "…" : "")) > 228)
        { text = text[..^1]; truncated = true; }
        if (truncated) text += "…";
        canvas.DrawText(text, (256 - font.MeasureText(text)) / 2, baseline, font, paint);
    }
    public static IReadOnlyList<byte[]> Icons(bool dark)
        => Enumerable.Range(0, 7).Select(index => Icon(index, dark)).ToArray();
    private static byte[] Icon(int index, bool dark)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
        using var path = new SKPath();
        var fill = true;
        switch (index)
        {
            case 0: path.AddRect(SKRect.Create(6, 7, 3, 18)); path.MoveTo(24, 7); path.LineTo(10, 16); path.LineTo(24, 25); path.Close(); break;
            case 1: path.MoveTo(9, 6); path.LineTo(25, 16); path.LineTo(9, 26); path.Close(); break;
            case 2: path.AddRect(SKRect.Create(7, 6, 6, 20)); path.AddRect(SKRect.Create(19, 6, 6, 20)); break;
            case 3: path.AddRect(SKRect.Create(23, 7, 3, 18)); path.MoveTo(8, 7); path.LineTo(22, 16); path.LineTo(8, 25); path.Close(); break;
            case 4:
            case 5:
                path.MoveTo(16, 27); path.CubicTo(12, 24, 4, 17, 4, 11); path.CubicTo(4, 3, 13, 2, 16, 9);
                path.CubicTo(19, 2, 28, 3, 28, 11); path.CubicTo(28, 17, 20, 24, 16, 27); path.Close(); fill = index == 5; break;
            case 6:
                path.MoveTo(5, 8); path.LineTo(27, 8); path.MoveTo(5, 15); path.LineTo(27, 15);
                path.MoveTo(5, 22); path.LineTo(19, 22); path.MoveTo(23, 19); path.LineTo(27, 22); path.LineTo(23, 25); fill = false; break;
        }
        // Opposing narrow halo keeps glyphs legible when taskbar and application themes differ.
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = fill ? 2 : 4,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = dark ? SKColor.Parse("#252A3E") : SKColors.White };
        canvas.DrawPath(path, paint);
        paint.Color = index == 5 ? SKColor.Parse("#B574CF") : dark ? SKColors.White : SKColor.Parse("#252A3E");
        paint.Style = fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke; paint.StrokeWidth = 2;
        canvas.DrawPath(path, paint);
        return Copy(bitmap);
    }
    private static byte[] Copy(SKBitmap bitmap)
    { var bytes = new byte[bitmap.ByteCount]; Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length); return bytes; }
    private sealed class Pixels(SKBitmap bitmap) : ILockedFramebuffer
    {
        public nint Address => bitmap.GetPixels();
        public PixelSize Size => new(bitmap.Width, bitmap.Height);
        public int RowBytes => bitmap.RowBytes;
        public Vector Dpi => new(96, 96);
        public PixelFormat Format => PixelFormat.Bgra8888;
        public AlphaFormat AlphaFormat => AlphaFormat.Premul;
        public void Dispose() { }
    }
}
