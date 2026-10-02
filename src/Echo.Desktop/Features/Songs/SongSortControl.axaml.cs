using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Echo.Desktop.Features.Songs;

public partial class SongSortControl : UserControl
{
    public SongSortControl() => InitializeComponent();
    private void Select(object? sender, bool? direction = null, bool keepOpen = false)
    {
        if ((sender as Control)?.DataContext is not SongSortOption option || DataContext is not SongsViewModel model) return;
        model.SelectSort(option.Index, direction);
        if (!model.IsMultiSortEnabled && !keepOpen) SortButton.Flyout?.Hide();
    }
    private void OnSelect(object? sender, RoutedEventArgs e) => Select(sender);
    private void OnDirection(object? sender, RoutedEventArgs e)
    { if ((sender as Control)?.DataContext is SongSortOption option) Select(sender, !option.Descending, keepOpen: true); }
}
