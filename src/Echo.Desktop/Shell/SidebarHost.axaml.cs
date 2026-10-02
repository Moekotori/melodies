using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Shell;

// One retained navigation view, one finite compositor lane, no hover timer.
public partial class SidebarHost : UserControl
{
    private readonly MotionLane _motion = new();
    private ShellViewModel? _model;
    private Window? _window;
    private IInputElement? _returnFocus;
    private int _generation;
    private bool _wasOpen;
    private bool _focusRevealAfterClose;

    public SidebarHost()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (this.IsAttachedToVisualTree()) { Detach(); Attach(); } };
        PointerExited += (_, _) => { if (_model?.IsSidebarDrawerMode == true && !NavigationPanel.IsKeyboardFocusWithin) _model.CloseSidebarDrawerCommand.Execute(null); };
    }
    private void Attach()
    {
        _model = DataContext as ShellViewModel;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_model is null) return;
        _model.PropertyChanged += OnChanged;
        _model.Motion.PropertyChanged += OnMotionChanged;
        if (_window is not null)
        {
            _window.AddHandler(PointerPressedEvent, OnWindowPressed, RoutingStrategies.Tunnel);
            _window.AddHandler(KeyDownEvent, OnWindowKey, RoutingStrategies.Tunnel);
        }
        Settle();
    }
    private void Detach()
    {
        ++_generation; _motion.Cancel();
        if (_model is not null) { _model.PropertyChanged -= OnChanged; _model.Motion.PropertyChanged -= OnMotionChanged; }
        if (_window is not null)
        {
            _window.RemoveHandler(PointerPressedEvent, OnWindowPressed);
            _window.RemoveHandler(KeyDownEvent, OnWindowKey);
        }
        _model = null; _window = null; _returnFocus = null; _wasOpen = false; _focusRevealAfterClose = false;
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsSidebarDrawerOpen)) Transition();
        else if (e.PropertyName is nameof(ShellViewModel.IsSidebarDrawerMode) or nameof(ShellViewModel.IsSidebarSuppressed)
                 or nameof(ShellViewModel.IsSidebarExpanded)) Settle();
    }
    private async void Transition()
    {
        if (_model?.IsSidebarDrawerMode != true || !_model.Motion.CanAnimate) { Settle(); return; }
        var opening = _model.IsSidebarDrawerOpen;
        if (!opening) _returnFocus = null;
        var generation = ++_generation;
        var opacity = _wasOpen || _motion.IsRunning ? MotionLane.CurrentOpacity(NavigationPanel) : 0;
        var transform = _wasOpen || _motion.IsRunning ? MotionLane.Current(NavigationPanel) : MotionLane.At(x: -220);
        _motion.Cancel(); _wasOpen = opening;
        Width = 220; NavigationPanel.Width = 220; NavigationPanel.IsVisible = true;
        NavigationPanel.IsHitTestVisible = opening; NavigationPanel.IsEnabled = true;
        KeyboardNavigation.SetTabNavigation(NavigationPanel, opening ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.None);
        if (!opening && NavigationPanel.IsKeyboardFocusWithin) _window?.FocusManager?.Focus(null);
        EdgeReveal.IsVisible = false;
        await _motion.PlayAsync(NavigationPanel, opacity, opening ? 1 : 0,
            _model.Motion.UseSpatialMotion ? transform : MotionLane.At(),
            MotionLane.At(x: !opening && _model.Motion.UseSpatialMotion ? -220 : 0),
            _model.Motion.UseSpatialMotion ? opening ? MotionTokens.Reveal : MotionTokens.Exit : MotionTokens.Reduced, preserveGeometry: true);
        if (generation == _generation) Settle();
    }
    private void Settle()
    {
        ++_generation; _motion.Cancel();
        var suppressed = _model?.IsSidebarSuppressed != false;
        var drawer = _model?.IsSidebarDrawerMode == true;
        var open = !suppressed && (!drawer || _model!.IsSidebarDrawerOpen);
        if (!open) _returnFocus = null;
        if (!drawer) _focusRevealAfterClose = false;
        _wasOpen = drawer && open;
        IsVisible = !suppressed;
        Width = drawer ? open ? 220 : 22 : _model?.SidebarReservedWidth ?? 0;
        NavigationPanel.Width = drawer ? 220 : Width;
        NavigationPanel.IsVisible = open; NavigationPanel.IsHitTestVisible = open; NavigationPanel.IsEnabled = open;
        NavigationPanel.Opacity = 1; NavigationPanel.RenderTransform = MotionLane.At();
        KeyboardNavigation.SetTabNavigation(NavigationPanel, drawer ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.Continue);
        EdgeReveal.IsVisible = drawer && !open;
        if (EdgeReveal.IsVisible && _focusRevealAfterClose) { _focusRevealAfterClose = false; RevealButton.Focus(NavigationMethod.Tab); }
    }
    private void OnEdgeEntered(object? sender, PointerEventArgs e) => _model?.OpenSidebarDrawerCommand.Execute(null);
    private void OnRevealClicked(object? sender, RoutedEventArgs e) => OpenWithFocus();
    private void OpenWithFocus()
    {
        _returnFocus = _window?.FocusManager?.GetFocusedElement();
        _model?.OpenSidebarDrawerCommand.Execute(null);
        NavigationPanel.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyVisible && button.IsEnabled)?.Focus(NavigationMethod.Tab);
    }
    public void ToggleFromKeyboard()
    { if (_model?.IsSidebarDrawerOpen == true) CloseAndRestoreFocus(); else OpenWithFocus(); }
    public void CloseAndRestoreFocus()
    {
        var previous = _returnFocus as Control;
        _focusRevealAfterClose = previous is null || !previous.IsEffectivelyVisible;
        _model?.CloseSidebarDrawerCommand.Execute(null);
        if (previous is { IsEffectivelyVisible: true }) previous.Focus(NavigationMethod.Tab);
        _returnFocus = null;
    }
    private void OnWindowKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _model?.Shortcuts.IsRecording == true) return;
        if (_model?.IsSidebarDrawerMode != true) return;
        if (e.Key == Key.Escape && _model.IsSidebarDrawerOpen) { CloseAndRestoreFocus(); e.Handled = true; }
        if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { ToggleFromKeyboard(); e.Handled = true; }
    }
    private void OnWindowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_model?.IsSidebarDrawerOpen != true || new Avalonia.Rect(NavigationPanel.Bounds.Size).Contains(e.GetPosition(NavigationPanel))) return;
        _model.CloseSidebarDrawerCommand.Execute(null); _returnFocus = null;
    }
    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MotionPolicy.CanAnimate)) return;
        if (_model?.Motion.IsWindowVisible == false) _model.CloseSidebarDrawerCommand.Execute(null);
        Settle();
    }
}
