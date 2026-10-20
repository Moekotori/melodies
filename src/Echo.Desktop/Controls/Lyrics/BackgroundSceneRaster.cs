using SkiaSharp;

namespace Echo.Desktop.Controls.Lyrics;

// Render-thread resource shared by the view's retained draw operations. The shader runs at
// the source renderer's pixel budget, then a texture is scaled to the window (including DPI).
internal sealed class BackgroundSceneRaster : IDisposable
{
    private int references = 1;
    private SKSurface? surface;
    private SKImage? image;
    private SourceSceneFrame? rendered;
    private GRContext? graphics;
    private int width, height;
    private readonly SKPaint paint = new();

    public BackgroundSceneRaster Retain() { Interlocked.Increment(ref references); return this; }

    public void Draw(SourceSceneFrame frame, SKCanvas target, GRContext? context, SKRect bounds, double opacity)
    {
        if (surface is null || width != frame.Width || height != frame.Height || !ReferenceEquals(graphics, context))
        {
            image?.Dispose(); image = null;
            surface?.Dispose();
            width = frame.Width; height = frame.Height; graphics = context;
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            surface = context is null ? SKSurface.Create(info) : SKSurface.Create(context, true, info);
            if (surface is null) throw new InvalidOperationException("Cannot allocate background render surface.");
            rendered = null;
        }
        if (!ReferenceEquals(rendered, frame))
        {
            // Drop the snapshot before drawing so Skia can reuse the surface without copying.
            image?.Dispose(); image = null;
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.DrawPicture(frame.Picture);
            image = surface.Snapshot(); rendered = frame;
        }
        paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(opacity * 255), 0, 255));
        target.DrawImage(image!, bounds, new SKSamplingOptions(SKFilterMode.Linear), paint);
    }

    public void Dispose()
    {
        if (Interlocked.Decrement(ref references) != 0) return;
        image?.Dispose(); surface?.Dispose(); paint.Dispose();
        image = null; surface = null; rendered = null; graphics = null;
    }
}
