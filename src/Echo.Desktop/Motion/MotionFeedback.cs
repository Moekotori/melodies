using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Echo.Desktop.Motion;

// Per-realized-control transitions; detached controls retain no policy subscription.
public sealed class MotionFeedback : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<MotionFeedback, Control, bool>("Enabled");
    private static readonly AttachedProperty<FeedbackState?> StateProperty =
        AvaloniaProperty.RegisterAttached<MotionFeedback, Control, FeedbackState?>("State");
    static MotionFeedback() => EnabledProperty.Changed.AddClassHandler<Control>((control, change) =>
    {
        control.GetValue(StateProperty)?.Dispose();
        control.SetValue(StateProperty, GetEnabled(control) ? new FeedbackState(control) : null);
    });
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    private sealed class FeedbackState : IDisposable
    {
        private readonly Control _control;
        private MotionPolicy? _policy;
        public FeedbackState(Control control)
        {
            _control = control;
            control.AttachedToVisualTree += OnAttached;
            control.DetachedFromVisualTree += OnDetached;
            control.PropertyChanged += OnControlChanged;
            if (control.IsAttachedToVisualTree()) Attach();
        }
        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args) => Attach();
        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args) => Detach();
        private void OnControlChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        { if (args.Property == MotionScope.PolicyProperty && _control.IsAttachedToVisualTree()) Attach(); }
        private void OnPolicyChanged(object? sender, PropertyChangedEventArgs args)
        { if (args.PropertyName == nameof(MotionPolicy.CanAnimate)) Apply(); }
        private void Attach()
        {
            Detach();
            _policy = MotionScope.GetPolicy(_control);
            if (_policy is not null) _policy.PropertyChanged += OnPolicyChanged;
            Apply();
        }
        private void Apply()
        {
            _control.Transitions = null;
            _control.Classes.Set("motion-spatial", _policy?.UseSpatialMotion == true);
            if (_control is ToggleSwitch toggle)
                toggle.KnobTransitions = _policy?.UseSpatialMotion == true ? new Transitions
                {
                    new DoubleTransition { Property = Canvas.LeftProperty, Duration = MotionTokens.Feedback, Easing = MotionTokens.EnterEasing }
                } : new Transitions();
            if (_policy?.CanAnimate != true) return;
            var duration = _policy.Duration(_policy.UseSpatialMotion ? MotionTokens.Feedback : MotionTokens.Reduced);
            var transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = MotionTokens.EnterEasing },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = MotionTokens.EnterEasing }
            };
            if (_control is TemplatedControl)
            {
                transitions.Add(new BrushTransition { Property = TemplatedControl.BackgroundProperty, Duration = duration });
                transitions.Add(new BrushTransition { Property = TemplatedControl.ForegroundProperty, Duration = duration });
                transitions.Add(new BrushTransition { Property = TemplatedControl.BorderBrushProperty, Duration = duration });
            }
            if (_control is Border)
            {
                transitions.Add(new BrushTransition { Property = Border.BackgroundProperty, Duration = duration });
                transitions.Add(new BrushTransition { Property = Border.BorderBrushProperty, Duration = duration });
            }
            _control.Transitions = transitions;
        }
        private void Detach()
        {
            if (_policy is not null) _policy.PropertyChanged -= OnPolicyChanged;
            _policy = null;
            _control.Transitions = null;
            if (_control is ToggleSwitch toggle) toggle.KnobTransitions = new Transitions();
            _control.Classes.Set("motion-spatial", false);
        }
        public void Dispose()
        {
            Detach();
            _control.AttachedToVisualTree -= OnAttached;
            _control.DetachedFromVisualTree -= OnDetached;
            _control.PropertyChanged -= OnControlChanged;
        }
    }
}
