using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Echo.Desktop.Controls;

// A decorative scroll envelope, matching the legacy list control; not audio samples.
public sealed class WaveformScrollBar : Control
{
    public static readonly StyledProperty<ListBox?> TargetProperty = AvaloniaProperty.Register<WaveformScrollBar, ListBox?>(nameof(Target));
    public ListBox? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    private ScrollViewer? scroll;
    private IPointer? pointer;
    public WaveformScrollBar() { Focusable = true; Cursor = new Cursor(StandardCursorType.Hand); }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); BindTarget(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { Release(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == TargetProperty && this.IsAttachedToVisualTree()) BindTarget(); }
    private void BindTarget()
    {
        Release(); Target?.ApplyTemplate(); scroll = Target?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is not null) scroll.ScrollChanged += Scrolled;
        InvalidateVisual();
    }
    private void Scrolled(object? s, ScrollChangedEventArgs e) => InvalidateVisual();
    private void Release() { if (scroll is not null) scroll.ScrollChanged -= Scrolled; scroll = null; pointer?.Capture(null); pointer = null; }
    private void Move(double y)
    { if (scroll is not null && Bounds.Height > 0) scroll.Offset = new(scroll.Offset.X, Math.Clamp(y / Bounds.Height, 0, 1) * Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)); }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    { if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; Focus(); pointer = e.Pointer; pointer.Capture(this); Move(e.GetPosition(this).Y); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { if (pointer == e.Pointer) { Move(e.GetPosition(this).Y); e.Handled = true; } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { if (pointer != e.Pointer) return; Move(e.GetPosition(this).Y); pointer = null; e.Pointer.Capture(null); e.Handled = true; }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { pointer = null; base.OnPointerCaptureLost(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (scroll is null) return;
        var max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        var next = e.Key switch { Key.Home => 0, Key.End => max, Key.Up => scroll.Offset.Y - 48, Key.Down => scroll.Offset.Y + 48,
            Key.PageUp => scroll.Offset.Y - scroll.Viewport.Height * .9, Key.PageDown => scroll.Offset.Y + scroll.Viewport.Height * .9, _ => double.NaN };
        if (!double.IsFinite(next)) return; scroll.Offset = new(scroll.Offset.X, Math.Clamp(next, 0, max)); e.Handled = true;
    }
    public override void Render(DrawingContext context)
    {
        if (scroll is null || scroll.Extent.Height <= scroll.Viewport.Height || Bounds.Height <= 0) return;
        var start = scroll.Offset.Y / scroll.Extent.Height; var end = (scroll.Offset.Y + scroll.Viewport.Height) / scroll.Extent.Height;
        var count = Math.Clamp((int)(Bounds.Height / 5), 1, 96); var step = Bounds.Height / count;
        var brush = this.FindResource("EchoAccent") as IBrush ?? Brushes.Gray;
        for (var i = 0; i < count; i++)
        {
            var ratio = (i + .5) / count; var active = ratio >= start && ratio <= end;
            var width = active ? Bounds.Width * (.55 + .4 * Math.Sin((ratio - start) / Math.Max(.001, end - start) * Math.PI)) : Bounds.Width * .25;
            using (context.PushOpacity(active ? .9 : .22)) context.FillRectangle(brush, new Rect((Bounds.Width - width) / 2, i * step, width, 2));
        }
    }
}
