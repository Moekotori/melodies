using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace Echo.Desktop.Motion;

// Owns only the current view and one outgoing view. Superseded views detach immediately.
// The surface brush is drawn once behind both, so a crossing never doubles a translucent tint.
public class MotionPresenter : Grid
{
    public static readonly StyledProperty<Control?> ContentProperty =
        AvaloniaProperty.Register<MotionPresenter, Control?>(nameof(Content));
    public static readonly StyledProperty<MotionAxis> AxisProperty =
        AvaloniaProperty.Register<MotionPresenter, MotionAxis>(nameof(Axis), MotionAxis.Horizontal);
    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<MotionPresenter, IBrush?>(nameof(SurfaceBrush));
    private readonly MotionLane _enter = new();
    private readonly MotionLane _exit = new();
    private Border? _current;
    private Border? _outgoing;
    private Task _retiring = Task.CompletedTask;
    private bool _retired;
    private MotionDirection _direction;
    private bool _instant;
    private MotionPolicy? _policy;
    private int _generation;
    public Control? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public MotionAxis Axis { get => GetValue(AxisProperty); set => SetValue(AxisProperty, value); }
    public IBrush? SurfaceBrush { get => GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }
    public bool IsTransitioning => _enter.IsRunning || _exit.IsRunning;
    public event Action<Control>? ContentRetired;

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
        if (change.Property == SurfaceBrushProperty) Background = SurfaceBrush;
        if (change.Property == MotionScope.PolicyProperty) ObservePolicy();
        if (change.Property == IsVisibleProperty && !IsVisible) Settle();
    }

    // Direction picks the scene side; animate: false swaps in place (e.g. a host already moving).
    public void Present(Control? content, MotionDirection direction, bool animate = true)
    {
        _direction = direction;
        _instant = !animate;
        if (_retired && ReferenceEquals(Content, content)) ChangeContent();
        else Content = content;
        _direction = MotionDirection.None;
        _instant = false;
    }

    // Starts the current view's exit before its replacement is built, so the compositor keeps it
    // moving while the UI thread constructs the next page. Completes once that exit is running.
    public Task RetireAsync(MotionDirection direction)
    {
        if (_current is not { } old || MotionScope.GetPolicy(this) is not { CanAnimate: true } || !this.IsAttachedToVisualTree())
            return Task.CompletedTask;
        var version = ++_generation;
        var opacity = MotionLane.CurrentOpacity(old);
        var transform = MotionLane.Current(old);
        _enter.Cancel();
        _exit.Cancel();
        RemoveOutgoing();
        _current = null;
        Isolate(old);
        _outgoing = old;
        _retired = true;
        _retiring = AwaitRetire(version, PlayExit(old, opacity, transform, MotionScene.Page(direction, Axis)));
        return _exit.Started;
    }
    private async Task AwaitRetire(int version, Task exit)
    {
        await exit;
        if (version == _generation) Settle();
    }

    private async void ChangeContent()
    {
        var version = ++_generation;
        var scene = MotionScene.Page(_direction, Axis);
        var instant = _instant;
        var old = _current;
        var oldOpacity = old is null ? 1 : MotionLane.CurrentOpacity(old);
        var oldTransform = old is null ? MotionLane.At() : MotionLane.Current(old);
        _enter.Cancel();
        // A view retired ahead of this content keeps its running exit; anything older leaves now.
        var retiring = _retired ? _retiring : Task.CompletedTask;
        if (!_retired) { _exit.Cancel(); RemoveOutgoing(); }
        _retired = false;
        _current = Content is null ? null : new Border { Child = Content, RenderTransform = MotionLane.At() };
        if (old is not null) { Isolate(old); _outgoing = old; }
        if (_current is not null) Children.Add(_current);
        Stack(scene);
        if (instant || MotionScope.GetPolicy(this) is not { CanAnimate: true } || !this.IsAttachedToVisualTree())
        { Settle(); return; }
        var incoming = _current is null ? Task.CompletedTask : _enter.PlayAsync(_current, 0, 1,
            scene.Enter.Pose, MotionLane.At(), scene.Enter.Duration, scene.Enter.Move,
            fadeEasing: _outgoing is null ? null : scene.Enter.Fade);
        var outgoing = old is null ? retiring : PlayExit(old, oldOpacity, oldTransform, scene);
        await Task.WhenAll(incoming, outgoing);
        if (version == _generation) Settle();
    }
    private Task PlayExit(Border old, double opacity, TransformOperations transform, MotionScene scene) =>
        _exit.PlayAsync(old, opacity, 0, transform, scene.Exit.Pose, scene.Exit.Duration, scene.Exit.Move, fadeEasing: scene.Exit.Fade);
    private void Isolate(Border old)
    {
        old.IsHitTestVisible = false;
        // Keyboard focus must leave the retiring tree immediately as well.
        KeyboardNavigation.SetTabNavigation(old, KeyboardNavigationMode.None);
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focus && old.IsVisualAncestorOf(focus))
            Focus();
    }
    private void Stack(MotionScene scene)
    {
        if (_current is not null) _current.ZIndex = scene.OutgoingAbove ? 0 : 1;
        if (_outgoing is not null) _outgoing.ZIndex = scene.OutgoingAbove ? 1 : 0;
    }
    public void Settle()
    {
        ++_generation;
        _enter.Cancel();
        _exit.Cancel();
        _retired = false;
        _retiring = Task.CompletedTask;
        RemoveOutgoing();
        if (_current is not null) { _current.Opacity = 1; _current.RenderTransform = MotionLane.At(); }
    }
    public async void Reveal()
    {
        Settle();
        if (_current is not { } current || MotionScope.GetPolicy(this) is not { CanAnimate: true }) return;
        var generation = _generation;
        var scene = MotionScene.Page(MotionDirection.None);
        await _enter.PlayAsync(current, 0, 1, scene.Enter.Pose, MotionLane.At(), scene.Enter.Duration, scene.Enter.Move);
        if (generation == _generation) Settle();
    }
    private void RemoveOutgoing()
    {
        if (_outgoing is null) return;
        Children.Remove(_outgoing);
        var content = _outgoing.Child;
        _outgoing.Child = null;
        _outgoing = null;
        if (content is not null) ContentRetired?.Invoke(content);
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
