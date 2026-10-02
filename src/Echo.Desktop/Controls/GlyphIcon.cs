using Avalonia;
using Avalonia.Media;

namespace Echo.Desktop.Controls;

// Original source application's Lucide geometry, using the native mask renderer.
public sealed class GlyphIcon : EchoIcon
{
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<GlyphIcon, IBrush?>(nameof(Stroke));
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public GlyphIcon() { Scope = "glyph"; StrokeWidth = 2; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StrokeProperty) BaseBrush = Stroke;
    }
}
