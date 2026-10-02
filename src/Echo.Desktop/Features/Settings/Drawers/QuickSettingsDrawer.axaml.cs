using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings.Drawers;

// Window-owned overlay. Only the open drawer owns a view; finite compositor motion.
public partial class QuickSettingsDrawer : UserControl
{
    private readonly MotionLane _motion = new();
    private ShellViewModel? _model;
    private Window? _window;
    private IInputElement? _returnFocus;
    private int _generation;
    private string _shownKind = "";
    public bool IsTransitioning => _motion.IsRunning;
    public QuickSettingsDrawer()
    {
        InitializeComponent(); IsVisible = false;
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (this.IsAttachedToVisualTree()) { Detach(); Attach(); } };
        SizeChanged += (_, _) => DrawerPanel.Width = Math.Min(440, Math.Max(0, Bounds.Width - 12));
        KeyboardNavigation.SetTabNavigation(DrawerPanel, KeyboardNavigationMode.Cycle);
    }
    private void Attach()
    {
        _model = DataContext as ShellViewModel; _window = TopLevel.GetTopLevel(this) as Window;
        if (_model is null) return;
        _model.PropertyChanged += OnChanged; _model.Motion.PropertyChanged += OnPolicyChanged;
        _window?.AddHandler(KeyDownEvent, OnWindowKey, RoutingStrategies.Tunnel);
        Settle();
    }
    private void Detach()
    {
        ++_generation; _motion.Cancel(); ReleaseContent();
        if (_model is not null) { _model.PropertyChanged -= OnChanged; _model.Motion.PropertyChanged -= OnPolicyChanged; }
        _window?.RemoveHandler(KeyDownEvent, OnWindowKey);
        _model = null; _window = null; _returnFocus = null; IsVisible = false;
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.QuickSettingsKind)) Transition();
        if (e.PropertyName == nameof(ShellViewModel.NavigationVersion)) _model?.CloseQuickSettingsCommand.Execute(null);
    }
    private void PrepareContent()
    {
        if (_model is null || _shownKind == _model.QuickSettingsKind) return;
        ReleaseContent(); _shownKind = _model.QuickSettingsKind;
        DrawerContent.Content = _shownKind == "audio" ? new AudioSettingsDrawerView(_model) : new LyricsSettingsDrawerView(_model);
    }
    private void ReleaseContent()
    {
        if (DrawerContent.Content is IDisposable lifetime) lifetime.Dispose();
        DrawerContent.Content = null; _shownKind = "";
    }
    private async void Transition()
    {
        if (_model is null) return;
        var opening = _model.IsQuickSettingsOpen;
        var generation = ++_generation;
        var opacity = IsVisible ? MotionLane.CurrentOpacity(DrawerPanel) : 0;
        var transform = IsVisible ? MotionLane.Current(DrawerPanel) : MotionLane.At(x: DrawerPanel.Width);
        _motion.Cancel();
        if (opening)
        {
            if (!IsVisible) _returnFocus = _window?.FocusManager?.GetFocusedElement();
            PrepareContent();
        }
        IsVisible = true; IsHitTestVisible = opening; DrawerPanel.IsEnabled = opening;
        if (opening) CloseDrawerButton.Focus(NavigationMethod.Tab);
        else RestoreFocus();
        var policy = _model.Motion;
        await _motion.PlayAsync(DrawerPanel, opacity, opening ? 1 : 0,
            policy.UseSpatialMotion ? transform : MotionLane.At(), MotionLane.At(x: !opening && policy.UseSpatialMotion ? DrawerPanel.Width : 0),
            !policy.CanAnimate ? TimeSpan.Zero : policy.UseSpatialMotion ? opening ? MotionTokens.Reveal : MotionTokens.Exit : MotionTokens.Reduced);
        if (generation == _generation) Settle();
    }
    private void Settle()
    {
        ++_generation; _motion.Cancel();
        var open = _model?.IsQuickSettingsOpen == true;
        IsVisible = open; IsHitTestVisible = open; DrawerPanel.IsEnabled = open;
        DrawerPanel.Opacity = 1; DrawerPanel.RenderTransform = MotionLane.At();
        if (open) PrepareContent(); else { ReleaseContent(); _returnFocus = null; }
    }
    private void RestoreFocus()
    {
        if (_returnFocus is Control { IsEffectivelyVisible: true } previous) previous.Focus(NavigationMethod.Tab);
        else _window?.FocusManager?.Focus(null);
        _returnFocus = null;
    }
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    { _model?.CloseQuickSettingsCommand.Execute(null); e.Handled = true; }
    private void OnCloseClicked(object? sender, RoutedEventArgs e) => _model?.CloseQuickSettingsCommand.Execute(null);
    private void OnWindowKey(object? sender, KeyEventArgs e)
    { if (_model?.IsQuickSettingsOpen == true && e.Key == Key.Escape) { _model.CloseQuickSettingsCommand.Execute(null); e.Handled = true; } }
    private void OnPolicyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MotionPolicy.CanAnimate)) return;
        if (_model?.Motion.IsWindowVisible == false) _model.CloseQuickSettingsCommand.Execute(null);
        Settle();
    }
}
