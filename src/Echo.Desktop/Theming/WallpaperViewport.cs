using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Echo.Desktop.Theming;

public sealed class WallpaperViewport : Control
{
    public Bitmap? Image { get; set; }
    public string Fit { get; set; } = "UniformToFill";
    public double PositionX { get; set; } = .5;
    public double PositionY { get; set; } = .5;
    public double Zoom { get; set; } = 1;
    public double Brightness { get; set; } = 1;
    public override void Render(DrawingContext context)
    {
        if (Image is not { } image || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var w = image.Size.Width; var h = image.Size.Height;
        var factor = (Fit is "Tile" or "Center" ? 1 : Fit == "Uniform" ? Math.Min(Bounds.Width / w, Bounds.Height / h) : Math.Max(Bounds.Width / w, Bounds.Height / h)) * Zoom;
        var dest = Fit == "Fill" ? new Rect(Bounds.Size) : new Rect((Bounds.Width - w * factor) * PositionX, (Bounds.Height - h * factor) * PositionY, w * factor, h * factor);
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            if (Fit == "Tile")
            {
                // Bound draw calls even for tiny images at large window sizes.
                var tw = Math.Max(32, w * factor); var th = Math.Max(32, h * factor);
                for (var y = -th * PositionY; y < Bounds.Height; y += th)
                    for (var x = -tw * PositionX; x < Bounds.Width; x += tw)
                        context.DrawImage(image, new Rect(image.Size), new Rect(x, y, tw, th));
            }
            else context.DrawImage(image, new Rect(image.Size), dest);
            if (Brightness != 1)
                using (context.PushOpacity(Math.Clamp(Math.Abs(Brightness - 1), 0, 1)))
                    context.FillRectangle(Brightness < 1 ? Brushes.Black : Brushes.White, new Rect(Bounds.Size));
        }
    }
}
