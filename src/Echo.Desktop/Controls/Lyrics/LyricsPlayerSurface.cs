using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Controls.Lyrics;

// A finite local morph redraws one capsule without relaying out the lyric stage.
public sealed class LyricsPlayerSurface : Control
{
    public static readonly StyledProperty<double> ExpansionProperty = AvaloniaProperty.Register<LyricsPlayerSurface, double>(nameof(Expansion), 1);
    public static readonly StyledProperty<double> CompactWidthProperty = AvaloniaProperty.Register<LyricsPlayerSurface, double>(nameof(CompactWidth), 320);
    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty = AvaloniaProperty.Register<LyricsPlayerSurface, IBrush?>(nameof(SurfaceBrush));
    public static readonly StyledProperty<bool> DarkProperty = AvaloniaProperty.Register<LyricsPlayerSurface, bool>(nameof(Dark), true);
    public double Expansion { get => GetValue(ExpansionProperty); set => SetValue(ExpansionProperty, value); }
    public double CompactWidth { get => GetValue(CompactWidthProperty); set => SetValue(CompactWidthProperty, value); }
    public IBrush? SurfaceBrush { get => GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }
    public bool Dark { get => GetValue(DarkProperty); set => SetValue(DarkProperty, value); }
    static LyricsPlayerSurface() => AffectsRender<LyricsPlayerSurface>(ExpansionProperty, CompactWidthProperty, SurfaceBrushProperty, DarkProperty);
    public Rect CapsuleBounds
    {
        get
        {
            var progress = Math.Clamp(Expansion, 0, 1);
            var compact = Math.Min(CompactWidth, Bounds.Width);
            var width = compact + (Bounds.Width - compact) * progress;
            var height = 34 + (Bounds.Height - 34) * progress;
            return new Rect((Bounds.Width - width) / 2, Bounds.Height - height, width, height);
        }
    }
    public override void Render(DrawingContext context)
    {
        var bounds = CapsuleBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        context.Custom(new LyricsGlassDrawOperation(bounds, Dark, SurfaceBrush?.ToImmutable()));
    }
}
