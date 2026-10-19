using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Echo.Desktop.Controls.Lyrics;
using Echo.Visuals;
using SkiaSharp;

internal static class FramePresentationSmoke
{
    public static void Run()
    {
        using var source = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++) source.SetPixel(x, y, new SKColor((byte)(x * 16), (byte)(y * 16), 96, (byte)(64 + x * 12)));
        using var legacy = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, source.GetPixels(),
            new PixelSize(16, 16), new Vector(96, 96), source.RowBytes);
        using var pool = new RasterFramePool();
        using var frame = pool.Capture(source)!;
        using var before = Render(new FrameControl(legacy, null));
        using var after = Render(new FrameControl(null, frame));
        if (!before.GetPixelSpan().SequenceEqual(after.GetPixelSpan()))
            throw new InvalidOperationException("Afterglow pixel scaling/alpha differs from the previous Bitmap path.");
        Console.WriteLine("PASS Afterglow image presentation: scaled translucent pixels match the previous Bitmap path");
    }

    private static SKBitmap Render(Control control)
    {
        control.Measure(new Size(47, 37)); control.Arrange(new Rect(0, 0, 47, 37));
        using var target = new RenderTargetBitmap(new PixelSize(47, 37), new Vector(96, 96));
        target.Render(control);
        using var encoded = new MemoryStream(); target.Save(encoded, PngBitmapEncoderOptions.Default);
        return SKBitmap.Decode(encoded.ToArray());
    }
    private sealed class FrameControl(Bitmap? bitmap, RasterImageFrame? frame) : Control
    {
        public override void Render(DrawingContext context)
        {
            if (bitmap is not null) context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(Bounds.Size));
            else context.Custom(new JizuraFrameDraw(frame!, new Rect(Bounds.Size)));
        }
    }
}
