using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Controls.Lyrics;

// A finite local morph redraws one capsule without relaying out the lyric stage.
public sealed class LyricsPlayerSurface : Control
{
    public static readonly StyledProperty<double> ExpansionProperty = AvaloniaProperty.Register<LyricsPlayerSurface, double>(nameof(Expansion), 1);
    public static readonly StyledProperty<double> CompactWidthProperty = AvaloniaProperty.Register<LyricsPlayerSurface, double>(nameof(CompactWidth), 340);
    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty = AvaloniaProperty.Register<LyricsPlayerSurface, IBrush?>(nameof(SurfaceBrush));
    public double Expansion { get => GetValue(ExpansionProperty); set => SetValue(ExpansionProperty, value); }
    public double CompactWidth { get => GetValue(CompactWidthProperty); set => SetValue(CompactWidthProperty, value); }
    public IBrush? SurfaceBrush { get => GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }
    static LyricsPlayerSurface() => AffectsRender<LyricsPlayerSurface>(ExpansionProperty, CompactWidthProperty, SurfaceBrushProperty);
    public Rect CapsuleBounds
    {
        get
        {
            var progress = Math.Clamp(Expansion, 0, 1);
            var compact = Math.Min(CompactWidth, Bounds.Width);
            var width = compact + (Bounds.Width - compact) * progress;
            var height = 40 + (Bounds.Height - 40) * progress;
            return new Rect((Bounds.Width - width) / 2, Bounds.Height - height, width, height);
        }
    }
    public override void Render(DrawingContext context)
    {
        var bounds = CapsuleBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
            context.DrawRectangle(SurfaceBrush, null, bounds, bounds.Height / 2, bounds.Height / 2);
    }
}
