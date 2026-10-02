using Avalonia;
using Avalonia.Controls;

namespace Echo.Desktop.Motion;

public sealed class MotionScope : AvaloniaObject
{
    public static readonly AttachedProperty<MotionPolicy?> PolicyProperty =
        AvaloniaProperty.RegisterAttached<MotionScope, Control, MotionPolicy?>("Policy", inherits: true);
    public static MotionPolicy? GetPolicy(Control control) => control.GetValue(PolicyProperty);
    public static void SetPolicy(Control control, MotionPolicy? value) => control.SetValue(PolicyProperty, value);
}
