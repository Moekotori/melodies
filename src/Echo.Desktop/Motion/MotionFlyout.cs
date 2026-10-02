using System.ComponentModel;
using Avalonia.Controls;

namespace Echo.Desktop.Motion;

public sealed class MotionFlyout : Flyout
{
    private readonly MotionLane _lane = new();
    private MotionPolicy? _policy;
    private bool _closing;
    private int _generation;
    public bool IsTransitioning => _lane.IsRunning;

    protected override void OnOpened()
    {
        _policy = Target is null ? null : MotionScope.GetPolicy(Target);
        if (_policy is not null) _policy.PropertyChanged += OnPolicyChanged;
        if (Popup.Child is { } presenter) MotionScope.SetPolicy(presenter, _policy);
        base.OnOpened();
        Enter();
    }
    private async void Enter()
    {
        ++_generation;
        _closing = false;
        if (Popup.Child is not { } presenter) return;
        _lane.Cancel();
        presenter.IsHitTestVisible = true;
        presenter.IsEnabled = true;
        if (_policy?.CanAnimate != true) { ResetPresenter(); return; }
        await _lane.PlayAsync(presenter, 0, 1,
            MotionLane.At(_policy.UseSpatialMotion ? MotionTokens.SurfaceTravel : 0), MotionLane.At(),
            _policy.UseSpatialMotion ? MotionTokens.Reveal : MotionTokens.Reduced);
    }
    protected override bool ShowAtCore(Control placementTarget, bool showAtPointer = false)
    {
        if (_closing && Target == placementTarget)
        {
            // Reversing a dismissal keeps this same popup and its content.
            ++_generation;
            _closing = false;
            var presenter = Popup.Child!;
            var opacity = MotionLane.CurrentOpacity(presenter);
            var transform = MotionLane.Current(presenter);
            _lane.Cancel();
            presenter.IsHitTestVisible = true;
            presenter.IsEnabled = true;
            Avalonia.Input.KeyboardNavigation.SetTabNavigation(presenter, Avalonia.Input.KeyboardNavigationMode.Continue);
            _ = _lane.PlayAsync(presenter, opacity, 1, transform, MotionLane.At(),
                _policy?.CanAnimate == true ? MotionTokens.Reveal : TimeSpan.Zero);
            return true;
        }
        return base.ShowAtCore(placementTarget, showAtPointer);
    }
    protected override bool HideCore(bool canCancel = true)
    {
        if (!canCancel || !IsOpen || _policy?.CanAnimate != true)
        { _lane.Cancel(); return base.HideCore(canCancel); }
        if (_closing) return false;
        // Keep the standard Closing cancellation contract before starting the visual exit.
        var closing = new CancelEventArgs();
        OnClosing(closing);
        if (closing.Cancel) return false;
        Exit();
        return false;
    }
    private async void Exit()
    {
        _closing = true;
        var generation = ++_generation;
        var presenter = Popup.Child!;
        var opacity = MotionLane.CurrentOpacity(presenter);
        var transform = MotionLane.Current(presenter);
        _lane.Cancel();
        presenter.IsHitTestVisible = false;
        TopLevel.GetTopLevel(presenter)?.FocusManager?.Focus(null);
        Avalonia.Input.KeyboardNavigation.SetTabNavigation(presenter, Avalonia.Input.KeyboardNavigationMode.None);
        await _lane.PlayAsync(presenter, opacity, 0, transform,
            MotionLane.At(_policy?.UseSpatialMotion == true ? MotionTokens.SurfaceTravel : 0),
            _policy?.UseSpatialMotion == true ? MotionTokens.Exit : MotionTokens.Reduced, MotionTokens.ExitEasing);
        if (generation == _generation) base.HideCore(false);
    }
    protected override void OnClosed()
    {
        ++_generation;
        _lane.Cancel();
        _closing = false;
        if (_policy is not null) _policy.PropertyChanged -= OnPolicyChanged;
        _policy = null;
        ResetPresenter();
        if (Popup.Child is { } presenter) MotionScope.SetPolicy(presenter, null);
        base.OnClosed();
    }
    private void OnPolicyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MotionPolicy.CanAnimate)) return;
        ++_generation;
        _lane.Cancel();
        if (_closing || _policy?.IsWindowVisible == false) base.HideCore(false);
        else ResetPresenter();
    }
    private void ResetPresenter()
    {
        if (Popup.Child is not { } presenter) return;
        presenter.Opacity = 1;
        presenter.RenderTransform = MotionLane.At();
        presenter.IsHitTestVisible = true;
        presenter.IsEnabled = true;
        Avalonia.Input.KeyboardNavigation.SetTabNavigation(presenter, Avalonia.Input.KeyboardNavigationMode.Continue);
    }
}
