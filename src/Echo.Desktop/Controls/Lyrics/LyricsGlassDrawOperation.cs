using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Echo.Desktop.Controls.Lyrics;

// Render-thread-only sampling. Each draw owns at most one local, transient GPU image (4 MiB cap).
internal sealed class LyricsGlassDrawOperation(Rect capsule, bool dark, IImmutableBrush? fallback) : ICustomDrawOperation
{
    private readonly SKPaint paint = new() { IsAntialias = true };
    private static readonly SKMaskFilter ShadowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3);
    public Rect Bounds => capsule.Inflate(12);
    public bool HitTest(Point point) => false;
    public bool Equals(ICustomDrawOperation? other) => other is LyricsGlassDrawOperation op
        && capsule == op.Capsule && dark == op.Dark && ReferenceEquals(fallback, op.Fallback);
    private Rect Capsule => capsule;
    private bool Dark => dark;
    private IImmutableBrush? Fallback => fallback;

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
        {
            context.DrawRectangle(fallback, null, capsule, capsule.Height / 2, capsule.Height / 2);
            return;
        }
        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;
        var shape = SKRect.Create((float)capsule.X, (float)capsule.Y, (float)capsule.Width, (float)capsule.Height);
        var radius = shape.Height / 2;
        var opacity = (byte)Math.Round(Math.Clamp(lease.CurrentOpacity, 0, 1) * 255);
        using var backdrop = Capture(lease, shape, out var placement);
        using var imageShader = backdrop?.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
            new SKSamplingOptions(SKFilterMode.Linear), placement);
        var effect = imageShader is null ? null : LyricsGlassShader.Effect;
        using var glass = effect?.ToShader(new SKRuntimeEffectUniforms(effect)
        {
            ["origin"] = new[] { shape.Left, shape.Top },
            ["size"] = new[] { shape.Width, shape.Height },
            ["dark"] = dark ? 1f : 0f
        }, new SKRuntimeEffectChildren(effect) { ["backdrop"] = imageShader! });

        DrawShadow(canvas, shape, radius, opacity);
        paint.Color = glass is null
            ? (dark ? new SKColor(39, 44, 55, opacity) : new SKColor(230, 235, 243, opacity))
            : SKColors.White.WithAlpha(opacity);
        paint.Shader = glass;
        canvas.DrawRoundRect(shape, radius, radius, paint);
        paint.Shader = null;
    }

    private static SKImage? Capture(ISkiaSharpApiLease lease, SKRect shape, out SKMatrix placement)
    {
        placement = SKMatrix.Identity;
        if (lease.SkSurface is not { } surface) return null;
        var matrix = lease.SkCanvas.TotalMatrix;
        if (!matrix.TryInvert(out var inverse)) return null;
        shape.Inflate(12, 12);
        var device = matrix.MapRect(shape);
        var clip = lease.SkCanvas.DeviceClipBounds;
        var rect = new SKRectI(Math.Max(clip.Left, (int)Math.Floor(device.Left)), Math.Max(clip.Top, (int)Math.Floor(device.Top)),
            Math.Min(clip.Right, (int)Math.Ceiling(device.Right)), Math.Min(clip.Bottom, (int)Math.Ceiling(device.Bottom)));
        if (rect.Width <= 0 || rect.Height <= 0 || (long)rect.Width * rect.Height > 1_048_576) return null;
        placement = SKMatrix.Concat(inverse, SKMatrix.CreateTranslation(rect.Left, rect.Top));
        return surface.Snapshot(rect);
    }

    private void DrawShadow(SKCanvas canvas, SKRect shape, float radius, byte opacity)
    {
        // One shared immutable filter; its 3-sigma falloff fits inside the 12-DIP draw margin.
        var shadow = shape;
        shadow.Offset(0, 2.5f);
        paint.Color = new SKColor(0, 0, 0, (byte)((dark ? 30 : 24) * opacity / 255));
        paint.MaskFilter = ShadowBlur;
        canvas.DrawRoundRect(shadow, radius, radius, paint);
        paint.MaskFilter = null;
    }

    public void Dispose() => paint.Dispose();
}
