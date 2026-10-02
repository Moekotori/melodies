using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Echo.Desktop.Shell;

public partial class Sidebar : UserControl
{
    private NavigationItem? _dragItem;
    private IPointer? _pointer;
    private Grid? _target;
    private Point _start;
    private bool _moved;
    private ShellViewModel? Model => DataContext as ShellViewModel;

    public Sidebar()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnHandlePressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnGripMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnGripReleased, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => ObserveModel();
        DetachedFromVisualTree += (_, _) => { CancelDrag(); UnobserveModel(); };
        DataContextChanged += (_, _) => { CancelDrag(); UnobserveModel(); if (this.IsAttachedToVisualTree()) ObserveModel(); };
    }
    private ShellViewModel? _observed;
    private void ObserveModel() { _observed = Model; if (_observed is not null) _observed.PropertyChanged += OnModelChanged; }
    private void UnobserveModel() { if (_observed is not null) _observed.PropertyChanged -= OnModelChanged; _observed = null; }
    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(ShellViewModel.IsSidebarEditing) && Model?.IsSidebarEditing != true) CancelDrag(); }
    private void OnEditClicked(object? sender, RoutedEventArgs e)
    {
        Model?.EditSidebarCommand.Execute(null);
        ExitEditing.Focus(NavigationMethod.Tab);
    }
    private void OnResetClicked(object? sender, RoutedEventArgs e) => Model?.ResetSidebarLayoutCommand.Execute(null);
    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control control) return;
        var handle = control.GetVisualAncestors().Prepend(control).OfType<Button>().FirstOrDefault(x => x.Classes.Contains("sidebar-handle"));
        if (handle is not null) OnGripPressed(handle, e);
    }
    private void OnNavigationClicked(object? sender, RoutedEventArgs e)
    {
        if (Model?.IsSidebarEditing != false || sender is not Control { DataContext: NavigationItem item }) return;
        Model.NavigateCommand.Execute(item.Id);
    }
    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Model?.IsSidebarEditing != true || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || sender is not Control { DataContext: NavigationItem item } grip) return;
        CancelDrag();
        _dragItem = item; _pointer = e.Pointer; _start = e.GetPosition(this); _moved = false;
        grip.Focus(); e.Pointer.Capture(grip); e.Handled = true;
    }
    private Grid? TargetAt(Point point)
    {
        return this.GetVisualDescendants().OfType<Grid>().FirstOrDefault(row =>
            row.Classes.Contains("navigation-row") && row.DataContext is NavigationItem item && item.Group == _dragItem?.Group
            && row.TranslatePoint(default, this) is { } origin && new Rect(origin, row.Bounds.Size).Contains(point));
    }
    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null || Model?.IsSidebarEditing != true) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _start.X) + Math.Abs(point.Y - _start.Y) < 5 && !_moved) return;
        _moved = true;
        _target?.Classes.Remove("drop-target");
        _target = TargetAt(point); _target?.Classes.Add("drop-target");
        // Event-driven scroll only while dragging; no retained timer.
        var scrollPoint = e.GetPosition(NavigationScroll);
        if (scrollPoint.Y < 24 || scrollPoint.Y > NavigationScroll.Bounds.Height - 24)
            NavigationScroll.Offset = new Vector(0, Math.Clamp(NavigationScroll.Offset.Y + (scrollPoint.Y < 24 ? -16 : 16), 0,
                Math.Max(0, NavigationScroll.Extent.Height - NavigationScroll.Viewport.Height)));
        e.Handled = true;
    }
    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragItem is null) return;
        var item = _dragItem;
        var target = _moved ? TargetAt(e.GetPosition(this))?.DataContext as NavigationItem : null;
        CancelDrag(); e.Pointer.Capture(null);
        if (target is not null) Model?.MoveSidebarItem(item, target);
        // Moving a row recreates its container. Restore focus to its handle.
        RestoreGripFocus(item);
        e.Handled = true;
    }
    private void RestoreGripFocus(NavigationItem item)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Model?.IsSidebarEditing == true && this.IsAttachedToVisualTree())
                this.GetVisualDescendants().OfType<Button>().FirstOrDefault(x => x.Classes.Contains("sidebar-handle") && x.DataContext == item)?.Focus();
        });
    }
    private void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CancelDrag();
    private void CancelDrag()
    {
        _target?.Classes.Remove("drop-target");
        var pointer = _pointer;
        _target = null; _dragItem = null; _pointer = null; _moved = false;
        pointer?.Capture(null);
    }
    private void OnGripKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down) || sender is not Control { DataContext: NavigationItem item }) return;
        Model?.MoveSidebarItemBy(item, e.Key == Key.Up ? -1 : 1); RestoreGripFocus(item); e.Handled = true;
    }
}
