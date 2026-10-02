using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Features.Folders;

public partial class FolderTrackList : UserControl
{
    private ScrollViewer? _scroll;
    private FoldersViewModel? Model => DataContext as FoldersViewModel;
    public FolderTrackList()
    {
        InitializeComponent();
        TrackList.TemplateApplied += (_, args) => BindScroll(args.NameScope.Find<ScrollViewer>("PART_ScrollViewer"));
        AttachedToVisualTree += (_, _) => { TrackList.ApplyTemplate(); BindScroll(TrackList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()); };
        DetachedFromVisualTree += (_, _) => BindScroll(null);
        DataContextChanged += (_, _) => OnSelection(null, null!);
    }
    private void BindScroll(ScrollViewer? scroll)
    {
        if (_scroll is not null) _scroll.PropertyChanged -= OnScrollChanged;
        _scroll = scroll;
        if (_scroll is not null) { _scroll.PropertyChanged += OnScrollChanged; UpdateHeaderWidth(); }
    }
    private void OnScrollChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    { if (args.Property == ScrollViewer.ViewportProperty) UpdateHeaderWidth(); }
    private void UpdateHeaderWidth()
    {
        // Reserve precisely the scroll viewport; no per-frame visual-tree discovery.
        var width = Math.Max(0, (_scroll?.Viewport.Width ?? TrackList.Bounds.Width) - 16);
        if (Math.Abs(HeaderColumns.Width - width) > .1 || double.IsNaN(HeaderColumns.Width)) HeaderColumns.Width = width;
    }
    private AlbumTrackItem[] Selection() => TrackList.SelectedItems?.OfType<AlbumTrackItem>().ToArray() ?? [];
    private void OnSelection(object? sender, SelectionChangedEventArgs args)
    {
        var count = Selection().Length; SelectionBar.IsVisible = count > 0;
        SelectionText.Text = Model?.Text.Format("folders.selected", count);
    }
    private void OnDoubleTap(object? sender, TappedEventArgs args)
    {
        if ((args.Source as Visual)?.GetVisualAncestors().OfType<Button>().Any() == true) return;
        if (TrackList.SelectedItem is AlbumTrackItem row) _ = Model?.ActAsync(start: row.Row);
    }
    private void OnKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && TrackList.SelectedItem is AlbumTrackItem row)
        { _ = Model?.ActAsync(start: row.Row); args.Handled = true; }
        if (args.Key == Key.Escape) { TrackList.SelectedItems?.Clear(); args.Handled = true; }
    }
    private void OnFavorite(object? sender, RoutedEventArgs args) => args.Handled = true;
    private void OnMenuPlay(object? sender, RoutedEventArgs args)
    { if (sender is MenuItem { DataContext: AlbumTrackItem row }) _ = Model?.ActAsync(start: row.Row); }
    private void OnMenuNext(object? sender, RoutedEventArgs args)
    { if (sender is MenuItem { DataContext: AlbumTrackItem row }) _ = Model?.ActAsync([row.Row], append: true, nextUp: true); }
    private void OnMenuAppend(object? sender, RoutedEventArgs args)
    { if (sender is MenuItem { DataContext: AlbumTrackItem row }) _ = Model?.ActAsync([row.Row], append: true); }
    private void OnPlaySelected(object? sender, RoutedEventArgs args) => _ = Model?.ActAsync(Selection().Select(row => row.Row).ToArray());
    private void OnNextSelected(object? sender, RoutedEventArgs args) => _ = Model?.ActAsync(Selection().Select(row => row.Row).ToArray(), append: true, nextUp: true);
    private void OnAppendSelected(object? sender, RoutedEventArgs args) => _ = Model?.ActAsync(Selection().Select(row => row.Row).ToArray(), append: true);
}
