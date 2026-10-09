using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Echo.Desktop.Theming;

public sealed class WallpaperViewport : Control
{
    private double _blur;
    private int _revision;
    private SoftKey _key;
    private RenderTargetBitmap? _low;
    private SKBitmap? _scratch;
    private SKBitmap? _blurred;
    private WriteableBitmap? _soft;
    private Bitmap? _image;
    public Bitmap? Image
    {
        get => _image;
        set
        {
            if (ReferenceEquals(_image, value)) return;
            _image = value;
            if (value is null) ReleaseSoft();
            InvalidateVisual();
        }
    }
    public string Fit { get; set; } = "UniformToFill";
    public double PositionX { get; set; } = .5;
    public double PositionY { get; set; } = .5;
    public double Zoom { get; set; } = 1;
    public int ContentRevision { get => _revision; set { if (_revision == value) return; _revision = value; InvalidateVisual(); } }
    public double Blur
    {
        get => _blur;
        set
        {
            if (Math.Abs(_blur - value) < 0.05) return;
            _blur = value;
            if (value < 0.5) ReleaseSoft();
            InvalidateVisual();
        }
    }
    public override void Render(DrawingContext context)
    {
        if (Image is not { } image || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            if (_blur < 0.5) Draw(context, image, Bounds.Width, Bounds.Height);
            else
            {
                var softened = Soften(image);
                if (ReferenceEquals(softened, image)) Draw(context, image, Bounds.Width, Bounds.Height);
                else context.DrawImage(softened, new Rect(Bounds.Size));
            }
        }
    }
    private Bitmap Soften(Bitmap image)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var divisor = Math.Clamp(Math.Max(_blur / 4, 1.75), 1.75, 8);
        var maxEdge = _blur < 6 ? 720 : _blur < 16 ? 480 : 320;
        var pxW = Bounds.Width * scaling / divisor;
        var pxH = Bounds.Height * scaling / divisor;
        var edge = Math.Max(pxW, pxH);
        if (edge > maxEdge) { var fit = maxEdge / edge; pxW *= fit; pxH *= fit; }
        var width = Math.Max(8, (int)Math.Round(pxW / 4d) * 4);
        var height = Math.Max(8, (int)Math.Round(pxH / 4d) * 4);
        var key = new SoftKey(image, _revision, width, height, (int)Math.Round(_blur * 4), Fit, PositionX, PositionY, Zoom);
        if (_soft is not null && _key == key) return _soft;
        try
        {
            if (_low is null || _low.PixelSize.Width != width || _low.PixelSize.Height != height)
            { _low?.Dispose(); _low = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)); }
            using (var dc = _low.CreateDrawingContext())
            {
                using (dc.PushRenderOptions(new RenderOptions { BitmapBlendingMode = BitmapBlendingMode.Source }))
                    dc.FillRectangle(Brushes.Transparent, new Rect(0, 0, width, height));
                Draw(dc, image, width, height);
            }
            if (_scratch is null || _scratch.Width != width || _scratch.Height != height)
            { _scratch?.Dispose(); _scratch = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul)); }
            _low.CopyPixels(new WallpaperGrade.Framebuffer(_scratch));
            if (_blurred is null || _blurred.Width != width || _blurred.Height != height)
            { _blurred?.Dispose(); _blurred = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul)); }
            var sigma = (float)Math.Clamp(_blur * width / Math.Max(1, Bounds.Width), 0.6, 32);
            WallpaperGrade.BlurInto(_scratch, _blurred, sigma);
            if (_soft is null || _soft.PixelSize.Width != width || _soft.PixelSize.Height != height)
            { _soft?.Dispose(); _soft = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul); }
            using (var locked = _soft.Lock())
                WallpaperGrade.Copy(_blurred.GetPixels(), _blurred.RowBytes, locked.Address, locked.RowBytes, width, height);
            _key = key;
            return _soft;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException)
        { return image; }
    }
    private void Draw(DrawingContext context, Bitmap image, double width, double height)
    {
        var sourceW = image.Size.Width; var sourceH = image.Size.Height;
        var factor = (Fit is "Tile" or "Center" ? 1 : Fit == "Uniform" ? Math.Min(width / sourceW, height / sourceH) : Math.Max(width / sourceW, height / sourceH)) * Zoom;
        if (Fit == "Tile")
        {
            var tileW = Math.Max(32, sourceW * factor); var tileH = Math.Max(32, sourceH * factor);
            for (var y = -tileH * PositionY; y < height; y += tileH)
                for (var x = -tileW * PositionX; x < width; x += tileW)
                    context.DrawImage(image, new Rect(image.Size), new Rect(x, y, tileW, tileH));
            return;
        }
        var dest = Fit == "Fill" ? new Rect(0, 0, width, height) : new Rect((width - sourceW * factor) * PositionX, (height - sourceH * factor) * PositionY, sourceW * factor, sourceH * factor);
        context.DrawImage(image, new Rect(image.Size), dest);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnDetachedFromVisualTree(e); ReleaseSoft(); }
    private void ReleaseSoft()
    { _low?.Dispose(); _scratch?.Dispose(); _blurred?.Dispose(); _soft?.Dispose(); _low = null; _scratch = null; _blurred = null; _soft = null; _key = default; }
    private readonly record struct SoftKey(Bitmap Image, int Revision, int Width, int Height, int Blur, string Fit, double X, double Y, double Zoom);
}
