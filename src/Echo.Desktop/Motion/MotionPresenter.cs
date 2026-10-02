using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Echo.Desktop.Motion;

// Owns only the current view and one outgoing view. Superseded views detach immediately.
public sealed class MotionPresenter : Grid
{
    public static readonly StyledProperty<Control?> ContentProperty =
        AvaloniaProperty.Register<MotionPresenter, Control?>(nameof(Content));
    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<MotionPresenter, IBrush?>(nameof(SurfaceBrush));
    private readonly MotionLane _enter = new();
    private readonly MotionLane _exit = new();
    private Border? _current;
    private Border? _outgoing;
    private MotionPolicy? _policy;
    private int _generation;
    public Control? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public IBrush? SurfaceBrush { get => GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }
    public bool IsTransitioning => _enter.IsRunning || _exit.IsRunning;

    public MotionPresenter()
    {
        ClipToBounds = true;
        Focusable = true;
        IsTabStop = false;
        AttachedToVisualTree += (_, _) => ObservePolicy();
        DetachedFromVisualTree += (_, _) => { UnobservePolicy(); Settle(); };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty) ChangeContent();
        if (change.Property == SurfaceBrushProperty)
        {
            if (_current is not null) _current.Background = SurfaceBrush;
            if (_outgoing is not null) _outgoing.Background = SurfaceBrush;
        }
        if (change.Property == MotionScope.PolicyProperty) ObservePolicy();
    }
    private async void ChangeContent()
    {
        var version = ++_generation;
        var old = _current;
        var oldOpacity = old is null ? 1 : MotionLane.CurrentOpacity(old);
        var oldTransform = old is null ? MotionLane.At() : MotionLane.Current(old);
        _enter.Cancel();
        _exit.Cancel();
        RemoveOutgoing();
        _current = Content is null ? null : new Border { Child = Content, Background = SurfaceBrush, RenderTransform = MotionLane.At() };
        if (old is not null)
        {
            old.IsHitTestVisible = false;
            // Keyboard focus must leave the retiring tree immediately as well.
            KeyboardNavigation.SetTabNavigation(old, KeyboardNavigationMode.None);
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focus && old.IsVisualAncestorOf(focus))
                Focus();
        }
        _outgoing = old;
        if (_current is not null) Children.Add(_current);
        if (MotionScope.GetPolicy(this) is not { CanAnimate: true } policy || !this.IsAttachedToVisualTree())
        { Settle(); return; }
        var duration = policy.UseSpatialMotion ? MotionTokens.Page : MotionTokens.Reduced;
        var incoming = _current is null ? Task.CompletedTask : _enter.PlayAsync(_current, 0, 1,
            MotionLane.At(policy.UseSpatialMotion ? MotionTokens.PageTravel : 0), MotionLane.At(), duration);
        var outgoing = old is null ? Task.CompletedTask : _exit.PlayAsync(old, oldOpacity, 0,
            oldTransform, MotionLane.At(),
            policy.UseSpatialMotion ? MotionTokens.Exit : MotionTokens.Reduced, MotionTokens.ExitEasing);
        await Task.WhenAll(incoming, outgoing);
        if (version == _generation) Settle();
    }
    public void Settle()
    {
        ++_generation;
        _enter.Cancel();
        _exit.Cancel();
        RemoveOutgoing();
        if (_current is not null) { _current.Opacity = 1; _current.RenderTransform = MotionLane.At(); }
    }
    public async void Reveal()
    {
        Settle();
        if (_current is not { } current || MotionScope.GetPolicy(this) is not { CanAnimate: true } policy) return;
        var generation = _generation;
        await _enter.PlayAsync(current, 0, 1,
            MotionLane.At(policy.UseSpatialMotion ? MotionTokens.PageTravel : 0), MotionLane.At(),
            policy.UseSpatialMotion ? MotionTokens.Page : MotionTokens.Reduced);
        if (generation == _generation) Settle();
    }
    private void RemoveOutgoing()
    {
        if (_outgoing is null) return;
        Children.Remove(_outgoing);
        _outgoing.Child = null;
        _outgoing = null;
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
