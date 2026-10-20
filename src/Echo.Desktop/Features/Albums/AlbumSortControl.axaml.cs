using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumSortControl : UserControl
{
    private TopLevel? popupWindow;
    public AlbumSortControl()
    {
        InitializeComponent();
        var flyout = (Flyout)SortButton.Flyout!;
        flyout.Opening += (_, _) => UpdatePopupHeight();
        flyout.Opened += (_, _) =>
        {
            popupWindow = TopLevel.GetTopLevel(this);
            if (popupWindow is not null) popupWindow.SizeChanged += OnWindowSizeChanged;
        };
        flyout.Closed += (_, _) => ReleasePopupWindow();
        DetachedFromVisualTree += (_, _) => ReleasePopupWindow();
    }
    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs args) => UpdatePopupHeight();
    private void UpdatePopupHeight()
    {
        if (TopLevel.GetTopLevel(this) is not { } top || SortButton.TranslatePoint(default, top) is not { } anchor) return;
        var below = top.Bounds.Height - anchor.Y - SortButton.Bounds.Height;
        var above = anchor.Y;
        var placeAbove = below < 160 && above > below;
        var flyout = (Flyout)SortButton.Flyout!;
        flyout.Placement = placeAbove ? PlacementMode.TopEdgeAlignedRight : PlacementMode.BottomEdgeAlignedRight;
        ((ScrollViewer)flyout.Content!).MaxHeight = Math.Clamp((placeAbove ? above : below) - 24, 0, 520);
    }
    private void ReleasePopupWindow()
    {
        if (popupWindow is not null) popupWindow.SizeChanged -= OnWindowSizeChanged;
        popupWindow = null;
    }
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
