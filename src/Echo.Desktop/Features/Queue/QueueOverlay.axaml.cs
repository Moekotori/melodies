using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Shell;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Features.Queue;

// One overlay above both main and lyrics stages. Closed visuals release artwork leases.
public partial class QueueOverlay : UserControl
{
    private ShellViewModel? model;
    private IInputElement? returnFocus;
    private Window? window;
    private int focusGeneration;
    public QueueOverlay()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (this.IsAttachedToVisualTree()) { Detach(); Attach(); } };
        SizeChanged += (_, _) => UpdateWidth();
        DrawerSurface.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && !DrawerSurface.IsVisible) DrawerSurface.Child = null;
        };
    }
    private void Attach()
    {
        model = DataContext as ShellViewModel;
        if (model is null) return;
        model.PropertyChanged += OnChanged;
        model.Motion.PropertyChanged += OnPolicyChanged;
        window = TopLevel.GetTopLevel(this) as Window;
        window?.AddHandler(KeyDownEvent, OnWindowKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        UpdateWidth(); UpdateOpen();
    }
    private void Detach()
    {
        ++focusGeneration;
        if (model is not null)
        {
            model.PropertyChanged -= OnChanged;
            model.Motion.PropertyChanged -= OnPolicyChanged;
        }
        window?.RemoveHandler(KeyDownEvent, OnWindowKey);
        DrawerSurface.IsOpen = OutsideSurface.IsOpen = false;
        DrawerSurface.Child = null;
        model = null; window = null; returnFocus = null;
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.IsQueueDrawerOpen)) UpdateOpen();
        if (args.PropertyName == nameof(ShellViewModel.NavigationVersion)) model?.CloseQueueDrawerCommand.Execute(null);
    }
    private void UpdateOpen()
    {
        var open = model?.IsQueueDrawerOpen == true;
        var generation = ++focusGeneration;
        var top = TopLevel.GetTopLevel(this);
        if (open && DrawerSurface.Child is null)
        {
            DrawerSurface.Child = new QueueDrawer();
        }
        if (open && returnFocus is null && !DrawerSurface.IsKeyboardFocusWithin)
            returnFocus = top?.FocusManager?.GetFocusedElement();
        DrawerSurface.IsOpen = OutsideSurface.IsOpen = open;
        // MotionSurface sets Continue while opening; the modal boundary owns cycling.
        KeyboardNavigation.SetTabNavigation(DrawerSurface, open ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.None);
        if (open)
            Dispatcher.UIThread.Post(() =>
            {
                if (focusGeneration == generation && DrawerSurface.Child is QueueDrawer drawer)
                    drawer.FindControl<Button>("CloseDrawerButton")?.Focus(NavigationMethod.Tab);
            });
        else
        {
            KeyboardNavigation.SetTabNavigation(DrawerSurface, KeyboardNavigationMode.None);
            var previous = returnFocus;
            Dispatcher.UIThread.Post(() =>
            {
                if (focusGeneration != generation) return;
                returnFocus = null;
                if (model is { IsQuickSettingsOpen: false, IsGlobalSearchOpen: false, IsSidebarDrawerOpen: false }
                    && previous is Control { IsEffectivelyVisible: true } control)
                    control.Focus(NavigationMethod.Tab);
            });
        }
    }
    private void UpdateWidth()
    {
        DrawerSurface.Width = Math.Min(440, Math.Max(0, Bounds.Width));
    }
    private void OnOutsidePressed(object? sender, PointerPressedEventArgs args)
    { model?.CloseQueueDrawerCommand.Execute(null); args.Handled = true; }
    private void OnPolicyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MotionPolicy.IsWindowVisible) && model?.Motion.IsWindowVisible == false)
        {
            model.CloseQueueDrawerCommand.Execute(null);
            DrawerSurface.Settle(); OutsideSurface.Settle();
        }
    }
    private void OnWindowKey(object? sender, KeyEventArgs args)
    {
        if (args.Handled || model?.IsQueueDrawerOpen != true || model.Shortcuts.IsRecording) return;
        if (args.Key == Key.F && args.KeyModifiers.HasFlag(KeyModifiers.Control) && DrawerSurface.Child is QueueDrawer drawer)
        { drawer.FocusSearch(); args.Handled = true; }
    }
}
