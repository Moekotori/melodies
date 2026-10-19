using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Echo.Desktop.Controls.Lyrics;

// A picture is immutable. The view and render operation hold separate references across the render-thread boundary.
internal sealed class SourceSceneFrame(SKPicture picture, int width, int height) : IDisposable
{
    private int references = 1;
    public SKPicture Picture { get; } = picture;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public SourceSceneFrame Retain() { Interlocked.Increment(ref references); return this; }
    public void Dispose() { if (Interlocked.Decrement(ref references) == 0) Picture.Dispose(); }
}
internal sealed class SourceSceneDraw(SourceSceneFrame frame, Rect bounds) : ICustomDrawOperation
{
    private readonly SourceSceneFrame frame = frame.Retain();
    public Rect Bounds { get; } = bounds;
    public bool Equals(ICustomDrawOperation? other) => other is SourceSceneDraw draw && ReferenceEquals(frame, draw.frame) && Bounds == draw.Bounds;
    public bool HitTest(Point point) => Bounds.Contains(point);
    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null) return;
        using var lease = feature.Lease(); var canvas = lease.SkCanvas;
        canvas.Save(); canvas.Translate((float)Bounds.X, (float)Bounds.Y);
        canvas.Scale((float)(Bounds.Width / frame.Width), (float)(Bounds.Height / frame.Height));
        canvas.DrawPicture(frame.Picture); canvas.Restore();
    }
    public void Dispose() => frame.Dispose();
}
