using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Echo.Desktop.Shell;

// Eight hit-test zones in window DIP units, independent of the user's content zoom.
// Owns only a window-state subscription; the native backend performs the resize.
public sealed class WindowResizeFrame : UserControl
{
    private Window? _window;

    public WindowResizeFrame()
    {
        Focusable = false;
        var zones = new Grid();
        Add(WindowDecorationsElementRole.ResizeN, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, 5);
        Add(WindowDecorationsElementRole.ResizeS, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, 5);
        Add(WindowDecorationsElementRole.ResizeW, HorizontalAlignment.Left, VerticalAlignment.Stretch, 5, double.NaN);
        Add(WindowDecorationsElementRole.ResizeE, HorizontalAlignment.Right, VerticalAlignment.Stretch, 5, double.NaN);
        Add(WindowDecorationsElementRole.ResizeNW, HorizontalAlignment.Left, VerticalAlignment.Top, 14, 14);
        Add(WindowDecorationsElementRole.ResizeNE, HorizontalAlignment.Right, VerticalAlignment.Top, 14, 14);
        Add(WindowDecorationsElementRole.ResizeSW, HorizontalAlignment.Left, VerticalAlignment.Bottom, 14, 14);
        Add(WindowDecorationsElementRole.ResizeSE, HorizontalAlignment.Right, VerticalAlignment.Bottom, 14, 14);
        Content = zones;

        void Add(WindowDecorationsElementRole role, HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height)
        {
            var zone = new Border { Background = Brushes.Transparent, Width = width, Height = height,
                HorizontalAlignment = horizontal, VerticalAlignment = vertical, Focusable = false };
            WindowDecorationProperties.SetElementRole(zone, role);
            zones.Children.Add(zone);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null) _window.PropertyChanged += OnWindowChanged;
        UpdateAvailability();
    }

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Window.CanResizeProperty) UpdateAvailability();
    }

    private void UpdateAvailability() => IsVisible = _window is { CanResize: true, WindowState: WindowState.Normal };

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null) _window.PropertyChanged -= OnWindowChanged;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }
}
