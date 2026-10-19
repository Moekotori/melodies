using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Playlists;

public partial class PlaylistTrackList : UserControl
{
    private ScrollViewer? scroll;
    public PlaylistTrackList()
    {
        InitializeComponent();
        TrackList.TemplateApplied += (_, args) => BindScroll(args.NameScope.Find<ScrollViewer>("PART_ScrollViewer"));
        AttachedToVisualTree += (_, _) => { TrackList.ApplyTemplate(); BindScroll(TrackList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()); };
        DetachedFromVisualTree += (_, _) => BindScroll(null);
    }
    private void BindScroll(ScrollViewer? value)
    {
        if (scroll is not null) scroll.PropertyChanged -= OnScrollChanged;
        scroll = value;
        if (scroll is not null) { scroll.PropertyChanged += OnScrollChanged; UpdateHeader(); }
    }
    private void OnScrollChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    { if (args.Property == ScrollViewer.ViewportProperty) UpdateHeader(); }
    private void UpdateHeader()
    {
        var width = Math.Max(0, (scroll?.Viewport.Width ?? TrackList.Bounds.Width) - 58);
        if (Math.Abs(HeaderColumns.Width - width) > 0.5 || double.IsNaN(HeaderColumns.Width)) HeaderColumns.Width = width;
    }
    private PlaylistsViewModel? Model => DataContext as PlaylistsViewModel;
    private PlaylistTrackItem[] Selected => TrackList.SelectedItems?.OfType<PlaylistTrackItem>().ToArray() ?? [];
    private void OnSelection(object? sender, SelectionChangedEventArgs args)
    { SelectionBar.IsVisible = Selected.Length > 0; SelectionText.Text = Model?.Text.Format("playlists.selected", Selected.Length); }
    private void OnDoubleTap(object? sender, TappedEventArgs args) { if (TrackList.SelectedItem is PlaylistTrackItem row) _ = Model?.PlayAsync([row]); }
    private void OnKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && Selected.Length > 0) { _ = Model?.PlayAsync(Selected); args.Handled = true; }
        if (args.Key == Key.Delete && Model?.IsManual == true && Selected.Length > 0) { _ = Model.RemoveAsync(Selected); args.Handled = true; }
    }
    private void OnPlaySelected(object? sender, RoutedEventArgs args) => _ = Model?.PlayAsync(Selected);
    private void OnNextSelected(object? sender, RoutedEventArgs args) => _ = Model?.PlayAsync(Selected, append: true, next: true);
    private void OnAppendSelected(object? sender, RoutedEventArgs args) => _ = Model?.PlayAsync(Selected, append: true);
    private void OnRemove(object? sender, RoutedEventArgs args) => _ = Model?.RemoveAsync(Selected);
    private void OnUp(object? sender, RoutedEventArgs args) { if (Selected.Length == 1) _ = Model?.MoveAsync(Selected[0], true); }
    private void OnDown(object? sender, RoutedEventArgs args) { if (Selected.Length == 1) _ = Model?.MoveAsync(Selected[0], false); }
    private void OnFavorite(object? sender, RoutedEventArgs args) => args.Handled = true;
    private void Menu(object? sender, bool append, bool next)
    { if (sender is MenuItem { DataContext: PlaylistTrackItem row }) _ = Model?.PlayAsync([row], append, next); }
    private void OnMenuPlay(object? sender, RoutedEventArgs args) => Menu(sender, false, false);
    private void OnMenuNext(object? sender, RoutedEventArgs args) => Menu(sender, true, true);
    private void OnMenuAppend(object? sender, RoutedEventArgs args) => Menu(sender, true, false);
}
