using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Controls;

// App zoom scales a bounded viewport. A page's desired height must never
// replace the window width or move title/player chrome outside that viewport.
public sealed class UiScaleViewport : LayoutTransformControl
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null || LayoutTransform is not ScaleTransform scale)
            return base.MeasureOverride(availableSize);
        var x = Math.Max(.01, scale.ScaleX);
        var y = Math.Max(.01, scale.ScaleY);
        Child.Measure(new Size(availableSize.Width / x, availableSize.Height / y));
        return new Size(Math.Min(availableSize.Width, Child.DesiredSize.Width * x),
            Math.Min(availableSize.Height, Child.DesiredSize.Height * y));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null || LayoutTransform is not ScaleTransform scale)
            return base.ArrangeOverride(finalSize);
        Child.Arrange(new Rect(0, 0, finalSize.Width / Math.Max(.01, scale.ScaleX),
            finalSize.Height / Math.Max(.01, scale.ScaleY)));
        return finalSize;
    }
}
