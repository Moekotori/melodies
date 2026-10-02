using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Queue;

public partial class QueueList : UserControl
{
    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<QueueList, bool>(nameof(Compact));
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    private QueueViewModel? model;
    private bool restoringSelection;
    private string[] selected = [];
    private Point? dragStart;
    private string[] dragItems = [];
    private ListBoxItem? dragTarget;
    public QueueList()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Classes.Set("compact", Compact || Bounds.Width < 720);
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
        DataContextChanged += (_, _) => { Detach(); if (this.IsAttachedToVisualTree()) Attach(); };
        Tracks.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        Tracks.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        Tracks.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == CompactProperty) Classes.Set("compact", Compact); }
    private void Attach()
    { model = DataContext as QueueViewModel; if (model is not null) model.PropertyChanged += OnModelChanged; }
    private void Detach()
    { if (model is not null) model.PropertyChanged -= OnModelChanged; model = null; dragStart = null; ClearDropTarget(); }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(QueueViewModel.Rows) || model is null) return;
        restoringSelection = true;
        try
        {
            Tracks.SelectedItems?.Clear();
            foreach (var row in model.Rows.Where(x => selected.Contains(x.ItemId))) Tracks.SelectedItems?.Add(row);
        }
        finally { restoringSelection = false; }
        model.SetSelection(Tracks.SelectedItems?.OfType<QueueRowViewModel>() ?? []);
    }
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (restoringSelection || model is null) return;
        var rows = Tracks.SelectedItems?.OfType<QueueRowViewModel>().ToArray() ?? [];
        selected = rows.Select(x => x.ItemId).ToArray(); model.SetSelection(rows);
    }
    private async void OnPlay(object? sender, TappedEventArgs args)
    { if (FindRow(args.Source) is { } row && model is not null) { args.Handled = true; await model.PlayAsync(row); } }
    private async void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (model is null) return;
        if (args.Key == Key.Delete && !model.ShowHistory) { args.Handled = true; await model.RemoveSelectedCommand.ExecuteAsync(null); }
        if (args.Key == Key.Enter && Tracks.SelectedItem is QueueRowViewModel row) { args.Handled = true; await model.PlayAsync(row); }
        if (args.Key == Key.A && args.KeyModifiers.HasFlag(KeyModifiers.Control)) { args.Handled = true; Tracks.SelectAll(); }
        if (args.Key == Key.Z && args.KeyModifiers.HasFlag(KeyModifiers.Control)) { args.Handled = true; await model.UndoCommand.ExecuteAsync(null); }
        if (args.KeyModifiers.HasFlag(KeyModifiers.Alt) && args.Key is Key.Up or Key.Down && Tracks.SelectedItem is QueueRowViewModel moving && !model.ShowHistory)
        { args.Handled = true; await model.MoveAsync(selected, moving.Index + (args.Key == Key.Up ? -1 : 2)); }
    }
    public void LocateCurrent()
    {
        if (model is null) return;
        var current = model.Rows.FirstOrDefault(x => x.IsCurrent);
        if (current is not null) { Tracks.ScrollIntoView(current); Tracks.SelectedItem = current; }
    }
    private static QueueRowViewModel? FindRow(object? source)
    {
        var control = source as Control;
        while (control is not null) { if (control.DataContext is QueueRowViewModel row) return row; control = control.Parent as Control; }
        return null;
    }
    private void OnRowMenu(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: QueueRowViewModel row } button || model is null) return;
        args.Handled = true;
        Tracks.SelectedItem = row; model.SetSelection([row]); selected = [row.ItemId];
        var play = new MenuItem { Header = "立即播放" }; play.Click += async (_, _) => await model.PlayAsync(row);
        var menu = new ContextMenu { Items = { play } };
        if (!model.ShowHistory)
        {
            menu.Items.Add(new MenuItem { Header = "下一首播放", Command = model.MoveNextCommand });
            var up = new MenuItem { Header = "上移" }; up.Click += async (_, _) => await model.MoveAsync([row.ItemId], row.Index - 1); menu.Items.Add(up);
            var down = new MenuItem { Header = "下移" }; down.Click += async (_, _) => await model.MoveAsync([row.ItemId], row.Index + 2); menu.Items.Add(down);
            menu.Items.Add(new MenuItem { Header = row.Track.RemoveAfterPlay ? "取消播后移除" : "播后移除", Command = row.Track.RemoveAfterPlay ? model.KeepAfterPlayCommand : model.RemoveAfterPlayCommand });
            menu.Items.Add(new MenuItem { Header = "移除", Command = model.RemoveSelectedCommand });
        }
        button.ContextMenu = menu; menu.Open(button);
    }
    private void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (model is null || model.ShowHistory || !args.GetCurrentPoint(Tracks).Properties.IsLeftButtonPressed || FindRow(args.Source) is not { } row) return;
        dragStart = args.GetPosition(Tracks); dragItems = selected.Contains(row.ItemId) ? selected : [row.ItemId];
    }
    private void OnMoved(object? sender, PointerEventArgs args)
    {
        if (dragStart is not { } start || !args.GetCurrentPoint(Tracks).Properties.IsLeftButtonPressed) return;
        if (Math.Abs(args.GetPosition(Tracks).Y - start.Y) <= 8) return;
        Tracks.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        ClearDropTarget();
        var hit = Tracks.InputHitTest(args.GetPosition(Tracks));
        dragTarget = (hit as Visual)?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (dragTarget is null) return;
        dragTarget.Classes.Set("dropTarget", true);
        dragTarget.Classes.Set("dropAfter", args.GetPosition(dragTarget).Y > dragTarget.Bounds.Height / 2);
    }
    private async void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        var start = dragStart; dragStart = null;
        var hit = Tracks.InputHitTest(args.GetPosition(Tracks));
        var targetControl = (hit as Visual)?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        var after = targetControl is not null && args.GetPosition(targetControl).Y > targetControl.Bounds.Height / 2;
        ClearDropTarget(); Tracks.Cursor = null;
        if (start is null || Math.Abs(args.GetPosition(Tracks).Y - start.Value.Y) <= 8 || model is null || FindRow(hit) is not { } target) return;
        args.Handled = true; await model.MoveAsync(dragItems, target.Index + (after ? 1 : 0));
    }
    private void ClearDropTarget()
    { dragTarget?.Classes.Set("dropTarget", false); dragTarget?.Classes.Set("dropAfter", false); dragTarget = null; }
}
