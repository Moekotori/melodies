using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Echo.Desktop.Motion;

// A small retained surface for notices and expandable groups, with real exit animation.
public sealed class MotionSurface : Decorator
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<MotionSurface, bool>(nameof(IsOpen));
    private readonly MotionLane _lane = new();
    private MotionPolicy? _policy;
    private int _generation;
    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public bool IsTransitioning => _lane.IsRunning;

    public MotionSurface()
    {
        IsVisible = false;
        AttachedToVisualTree += (_, _) => { ObservePolicy(); Settle(); };
        DetachedFromVisualTree += (_, _) => { UnobservePolicy(); Settle(); };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty) UpdateOpen();
        if (change.Property == MotionScope.PolicyProperty) ObservePolicy();
    }
    private async void UpdateOpen()
    {
        var generation = ++_generation;
        var opacity = IsVisible ? MotionLane.CurrentOpacity(this) : 0;
        var transform = IsVisible ? MotionLane.Current(this) : MotionLane.At(MotionTokens.SurfaceTravel);
        _lane.Cancel();
        IsHitTestVisible = IsOpen;
        IsEnabled = IsOpen;
        var policy = MotionScope.GetPolicy(this);
        if (!this.IsAttachedToVisualTree() || policy is not { CanAnimate: true }) { Settle(); return; }
        IsVisible = true;
        var spatial = policy.UseSpatialMotion;
        await _lane.PlayAsync(this, opacity, IsOpen ? 1 : 0,
            spatial ? transform : MotionLane.At(), MotionLane.At(!IsOpen && spatial ? MotionTokens.SurfaceTravel : 0),
            spatial ? IsOpen ? MotionTokens.Reveal : MotionTokens.Exit : MotionTokens.Reduced,
            IsOpen ? MotionTokens.EnterEasing : MotionTokens.ExitEasing);
        if (generation == _generation) Settle();
    }
    public void Settle()
    {
        ++_generation;
        _lane.Cancel();
        IsVisible = IsOpen;
        IsHitTestVisible = IsOpen;
        IsEnabled = IsOpen;
        Opacity = 1;
        RenderTransform = MotionLane.At();
    }
    private void ObservePolicy()
    {
        UnobservePolicy();
        _policy = MotionScope.GetPolicy(this);
        if (_policy is not null) _policy.PropertyChanged += OnPolicyChanged;
    }
    private void UnobservePolicy()
    {
        if (_policy is not null) _policy.PropertyChanged -= OnPolicyChanged;
        _policy = null;
    }
    private void OnPolicyChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(MotionPolicy.CanAnimate)) Settle(); }
}
