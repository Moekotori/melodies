using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Echo.Visuals;

namespace Echo.Desktop.Controls.Lyrics;

internal sealed class JizuraFrameDraw(RasterImageFrame image, Rect bounds) : ICustomDrawOperation
{
    private readonly RasterImageFrame frame = image.Retain();
    public Rect Bounds { get; } = bounds;
    public bool Equals(ICustomDrawOperation? other) => other is JizuraFrameDraw draw
        && ReferenceEquals(frame, draw.frame) && Bounds == draw.Bounds;
    public bool HitTest(Point point) => false;
    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null) return;
        using var lease = feature.Lease();
        lease.SkCanvas.DrawImage(frame.Image, new SkiaSharp.SKRect((float)Bounds.X, (float)Bounds.Y,
            (float)Bounds.Right, (float)Bounds.Bottom), new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear));
    }
    public void Dispose() => frame.Dispose();
}
