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
    public override void Render(DrawingContext context)
    {
        if (Image is not { } image || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var w = image.Size.Width; var h = image.Size.Height;
        var factor = Fit == "Uniform" ? Math.Min(Bounds.Width / w, Bounds.Height / h) : Math.Max(Bounds.Width / w, Bounds.Height / h);
        var dest = Fit == "Fill" ? new Rect(Bounds.Size) : new Rect((Bounds.Width - w * factor) * PositionX, (Bounds.Height - h * factor) * PositionY, w * factor, h * factor);
        using (context.PushClip(new Rect(Bounds.Size))) context.DrawImage(image, new Rect(image.Size), dest);
    }
}
