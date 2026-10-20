using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Echo.Desktop.Motion;

// Entrance for panes whose visibility a binding owns: tab panels, mode panes, empty and welcome
// states. Plays only when an attached pane turns visible, never on first realization (its page
// is already entering). Hiding stays immediate. One lane per enabled, realized pane.
public sealed class MotionReveal : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<MotionReveal, Control, bool>("Enabled");
    // Rise distance; full-area overlays use 0 so no edge strip shows while they fade in.
    public static readonly AttachedProperty<double> LiftProperty =
        AvaloniaProperty.RegisterAttached<MotionReveal, Control, double>("Lift", MotionTokens.SurfaceTravel);
    private static readonly AttachedProperty<RevealState?> StateProperty =
        AvaloniaProperty.RegisterAttached<MotionReveal, Control, RevealState?>("State");
    static MotionReveal()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            control.GetValue(StateProperty)?.Dispose();
            control.SetValue(StateProperty, GetEnabled(control) ? new RevealState(control) : null);
        });
        Visual.IsVisibleProperty.Changed.AddClassHandler<Control>((control, change) =>
            control.GetValue(StateProperty)?.Visibility(change.GetNewValue<bool>()));
    }
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
    public static double GetLift(Control control) => control.GetValue(LiftProperty);
    public static void SetLift(Control control, double value) => control.SetValue(LiftProperty, value);

    private sealed class RevealState : IDisposable
    {
        private readonly Control _control;
        private MotionLane? _lane;
        private int _generation;
        public RevealState(Control control)
        {
            _control = control;
            control.DetachedFromVisualTree += OnDetached;
        }
        public async void Visibility(bool visible)
        {
            var generation = ++_generation;
            if (!visible || !_control.IsAttachedToVisualTree() || MotionScope.GetPolicy(_control) is not { CanAnimate: true })
            { Settle(); return; }
            _lane ??= new MotionLane();
            await _lane.PlayAsync(_control, 0, 1, MotionLane.At(GetLift(_control)), MotionLane.At(), MotionTokens.Reveal,
                MotionTokens.EnterEasing);
            if (generation == _generation) Settle();
        }
        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args) { ++_generation; Settle(); }
        private void Settle()
        {
            _lane?.Cancel();
            _control.Opacity = 1;
            _control.RenderTransform = MotionLane.At();
        }
        public void Dispose()
        {
            ++_generation;
            Settle();
            _control.DetachedFromVisualTree -= OnDetached;
        }
    }
}
