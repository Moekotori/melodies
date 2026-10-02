using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Radio;

public partial class RadioStationRow : UserControl
{
    private RadioView? owner;
    public RadioStationRow()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => owner = this.GetVisualAncestors().OfType<RadioView>().FirstOrDefault();
        DetachedFromVisualTree += (_, _) => owner = null;
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 700;
            RowGrid.ColumnDefinitions = new(compact ? "36,*,0,0,0,32,32,32" : "44,*,128,104,90,36,36,36");
            FrequencyCell.IsVisible = GenreCell.IsVisible = CodecCell.IsVisible = !compact;
        };
    }
    private void OnSelect(object? sender, RoutedEventArgs args) { if (DataContext is RadioStationRowModel row && owner is not null) owner.Model.Selected = row.Station; }
    private async void OnFavorite(object? sender, RoutedEventArgs args) { if (DataContext is RadioStationRowModel row && owner is not null) await owner.Model.FavoriteAsync(row.Station); }
    private async void OnPlay(object? sender, RoutedEventArgs args) { if (DataContext is RadioStationRowModel row && owner is not null) await owner.Model.PlayAsync(row.Station); }
    private void OnEdit(object? sender, RoutedEventArgs args) { if (DataContext is RadioStationRowModel row) owner?.Model.Edit(row.Station); }
    private async void OnDelete(object? sender, RoutedEventArgs args) { if (DataContext is RadioStationRowModel row && owner is not null) await owner.Model.DeleteAsync(row.Station); }
    private async void OnCopy(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not RadioStationRowModel row || owner is null) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await owner.Model.RunAsync(() => clipboard.SetTextAsync(row.Station.Url));
    }
}
