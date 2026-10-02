using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Echo.Desktop.Controls;

// White Difference bitmap complements rendered pixels, including artwork, without a frame capture.
public sealed class InvertOverlay : Control
{
    private WriteableBitmap? white;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); white = new(new(1, 1), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var pixels = white.Lock(); Marshal.WriteInt32(pixels.Address, -1);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { white?.Dispose(); white = null; base.OnDetachedFromVisualTree(e); }
    public override void Render(DrawingContext context)
    {
        if (white is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        using (context.PushRenderOptions(new RenderOptions { BitmapBlendingMode = BitmapBlendingMode.Difference }))
            context.DrawImage(white, new Rect(white.Size), new Rect(Bounds.Size));
    }
}
