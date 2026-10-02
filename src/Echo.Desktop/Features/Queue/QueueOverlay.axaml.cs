using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Queue;

// One overlay above both main and lyrics stages. Closed visuals release artwork leases.
public partial class QueueOverlay : UserControl
{
    private ShellViewModel? model;
    private IInputElement? returnFocus;
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
        UpdateWidth(); UpdateOpen();
    }
    private void Detach()
    {
        ++focusGeneration;
        if (model is not null) model.PropertyChanged -= OnChanged;
        DrawerSurface.IsOpen = OutsideSurface.IsOpen = false;
        DrawerSurface.Child = null;
        model = null; returnFocus = null;
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
            returnFocus = top?.FocusManager?.GetFocusedElement();
            DrawerSurface.Child = new QueueDrawer();
            KeyboardNavigation.SetTabNavigation(DrawerSurface, KeyboardNavigationMode.Cycle);
        }
        DrawerSurface.IsOpen = OutsideSurface.IsOpen = open;
        if (open)
            Dispatcher.UIThread.Post(() =>
            {
                if (focusGeneration == generation && DrawerSurface.Child is QueueDrawer drawer)
                    drawer.FindControl<Button>("CloseDrawerButton")?.Focus(NavigationMethod.Tab);
            });
        else
        {
            KeyboardNavigation.SetTabNavigation(DrawerSurface, KeyboardNavigationMode.None);
            var previous = returnFocus; returnFocus = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (focusGeneration == generation && previous is Control { IsEffectivelyVisible: true } control)
                    control.Focus(NavigationMethod.Tab);
            });
        }
    }
    private void UpdateWidth()
    {
        var inset = Bounds.Width < 520 ? 12d : 28d;
        DrawerSurface.Margin = new Thickness(inset, Bounds.Height < 540 ? 12 : 36, inset, 4);
        DrawerSurface.Width = Math.Min(440, Math.Max(0, Bounds.Width - inset * 2));
    }
    private void OnOutsidePressed(object? sender, PointerPressedEventArgs args)
    { model?.CloseQueueDrawerCommand.Execute(null); args.Handled = true; }
}
