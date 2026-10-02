using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumSortControl : UserControl
{
    public AlbumSortControl() => InitializeComponent();
    private void OnSelect(object? sender, RoutedEventArgs args)
    {
        if (DataContext is AlbumsViewModel model && sender is Button { DataContext: AlbumSortOption { Field: { } field } })
        { model.SelectSort(field); SortButton.Flyout?.Hide(); SortButton.Focus(); }
    }
    private void OnDirection(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not AlbumsViewModel model || sender is not Button { DataContext: AlbumSortOption { Field: { } field } option }) return;
        var descending = !option.Descending;
        model.SelectSort(field, descending);
    }
}
