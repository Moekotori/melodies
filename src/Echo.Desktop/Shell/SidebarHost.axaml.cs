using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Shell;

// One retained navigation view. The drawer and its peek handle share one sliding strip.
public partial class SidebarHost : UserControl
{
    private const double DrawerWidth = 220;
    private const double DrawerPeek = 22;
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
        PointerExited += (_, _) => { if (_model is { IsSidebarDrawerMode: true, IsSidebarEditing: false } && !NavigationPanel.IsKeyboardFocusWithin) _model.CloseSidebarDrawerCommand.Execute(null); };
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
                 or nameof(ShellViewModel.IsSidebarExpanded) or nameof(ShellViewModel.IsSidebarEditing)) Settle();
    }
    private async void Transition()
    {
        if (_model?.IsSidebarDrawerMode != true || !_model.Motion.CanAnimate) { Settle(); return; }
        var opening = _model.IsSidebarDrawerOpen;
        if (!opening) _returnFocus = null;
        var generation = ++_generation;
        var spatial = _model.Motion.UseSpatialMotion;
        var running = _wasOpen || _motion.IsRunning;
        var slideFrom = spatial ? running ? MotionLane.Current(DrawerSlide) : MotionLane.At(x: -DrawerWidth) : MotionLane.At();
        var panelOpacity = running ? MotionLane.CurrentOpacity(NavigationPanel) : 0;
        _motion.Cancel(); _wasOpen = opening;
        Width = DrawerWidth; NavigationPanel.Width = DrawerWidth; NavigationPanel.IsVisible = true;
        NavigationPanel.Opacity = spatial ? 1 : panelOpacity;
        NavigationPanel.IsHitTestVisible = opening; NavigationPanel.IsEnabled = true;
        KeyboardNavigation.SetTabNavigation(NavigationPanel, opening ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.None);
        if (!opening && NavigationPanel.IsKeyboardFocusWithin) _window?.FocusManager?.Focus(null);
        EdgeReveal.IsHitTestVisible = false;
        EdgeReveal.IsEnabled = false;
        if (spatial)
        {
            // The handle sits on the drawer's trailing edge and parks in the peek when closed.
            DrawerSlide.Width = DrawerWidth + DrawerPeek;
            EdgeReveal.Margin = new Avalonia.Thickness(DrawerWidth, 0, 0, 0);
            EdgeReveal.IsVisible = true;
            DrawerSlide.Opacity = 1;
            await _motion.PlayAsync(DrawerSlide, 1, 1, slideFrom, MotionLane.At(x: opening ? 0 : -DrawerWidth),
                opening ? MotionTokens.Drawer : MotionTokens.DrawerExit, MotionTokens.DrawerEasing, preserveGeometry: true);
        }
        else
        {
            DrawerSlide.Width = double.NaN;
            DrawerSlide.RenderTransform = MotionLane.At();
            EdgeReveal.IsVisible = false;
            await _motion.PlayAsync(NavigationPanel, panelOpacity, opening ? 1 : 0, MotionLane.At(), MotionLane.At(), MotionTokens.Reduced, preserveGeometry: true);
        }
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
        Width = drawer ? open ? DrawerWidth : DrawerPeek : _model?.SidebarReservedWidth ?? 0;
        NavigationPanel.Width = drawer ? DrawerWidth : Width;
        NavigationPanel.IsVisible = open; NavigationPanel.IsHitTestVisible = open; NavigationPanel.IsEnabled = open;
        NavigationPanel.Opacity = 1; NavigationPanel.RenderTransform = MotionLane.At();
        DrawerSlide.Width = double.NaN; DrawerSlide.Opacity = 1; DrawerSlide.RenderTransform = MotionLane.At();
        EdgeReveal.Margin = new Avalonia.Thickness(0);
        EdgeReveal.IsHitTestVisible = true; EdgeReveal.IsEnabled = true;
        KeyboardNavigation.SetTabNavigation(NavigationPanel, drawer ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.Continue);
        EdgeReveal.IsVisible = drawer && !open;
        if (EdgeReveal.IsVisible && _focusRevealAfterClose) { _focusRevealAfterClose = false; RevealButton.Focus(NavigationMethod.Tab); }
    }
    private void OnEdgeEntered(object? sender, PointerEventArgs e) => _model?.OpenSidebarDrawerCommand.Execute(null);
    private void OnRevealClicked(object? sender, RoutedEventArgs e) => OpenWithFocus();
    private void OpenWithFocus()
    {
        _model?.CloseQuickSettingsCommand.Execute(null);
        _model?.CloseQueueDrawerCommand.Execute(null);
        _model?.CloseGlobalSearchCommand.Execute(null);
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
        if (e.Key == Key.Escape && _model?.IsSidebarEditing == true)
        { _model.ExitSidebarEditingCommand.Execute(null); e.Handled = true; return; }
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
