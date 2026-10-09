using System.Buffers;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Echo.Desktop.Theming;

// Brightness is a pixel multiply, so it stays correct at any zoom. Blur stays in the viewport.
internal static class WallpaperGrade
{
    public static bool IsNeutral(double brightness) => Math.Abs(brightness - 1) <= 0.004;

    public static void ApplyBrightness(SKBitmap bitmap, double brightness)
    {
        var pixels = bitmap.GetPixelSpan();
        var row = bitmap.Width * 4;
        for (var y = 0; y < bitmap.Height; y++) ApplyBrightness(pixels.Slice(y * bitmap.RowBytes, row), brightness);
    }

    public static void ApplyBrightness(Span<byte> bgra, double brightness)
    {
        var factor = (float)Math.Clamp(brightness, 0, 2);
        if (Math.Abs(factor - 1f) <= 0.004f) return;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            bgra[i] = Scale(bgra[i], factor);
            bgra[i + 1] = Scale(bgra[i + 1], factor);
            bgra[i + 2] = Scale(bgra[i + 2], factor);
        }
    }

    public static SKBitmap Snapshot(Bitmap bitmap)
    {
        var result = new SKBitmap(new SKImageInfo(bitmap.PixelSize.Width, bitmap.PixelSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.CopyPixels(new Framebuffer(result));
        return result;
    }

    public static WriteableBitmap ToBitmap(SKBitmap bitmap)
    {
        var result = new WriteableBitmap(new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var locked = result.Lock();
        Copy(bitmap.GetPixels(), bitmap.RowBytes, locked.Address, locked.RowBytes, bitmap.Width, bitmap.Height);
        return result;
    }

    public static void BlurInto(SKBitmap source, SKBitmap destination, float sigma)
    {
        using var surface = SKSurface.Create(destination.Info, destination.GetPixels(), destination.RowBytes)
            ?? throw new InvalidOperationException("背景模糊表面无法创建");
        using var filter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { ImageFilter = filter };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawBitmap(source, 0, 0, paint);
        surface.Flush();
    }

    public static void Copy(nint source, int sourceStride, nint destination, int destinationStride, int width, int height)
    {
        var row = width * 4;
        var buffer = ArrayPool<byte>.Shared.Rent(row);
        try
        {
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(source + y * sourceStride, buffer, 0, row);
                Marshal.Copy(buffer, 0, destination + y * destinationStride, row);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static byte Scale(byte value, float factor)
    {
        var scaled = (int)(value * factor + 0.5f);
        return scaled >= 255 ? (byte)255 : scaled <= 0 ? (byte)0 : (byte)scaled;
    }

    internal sealed class Framebuffer(SKBitmap bitmap) : ILockedFramebuffer
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
