using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumTrackRow : UserControl
{
    public event EventHandler? PlayRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? AddRequested;
    public AlbumTrackRow()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 650;
            Signal.IsVisible = !narrow;
            Columns.ColumnDefinitions[0].Width = new(narrow ? 28 : 46);
            Columns.ColumnDefinitions[2].Width = new(narrow ? 0 : 230);
            Columns.ColumnDefinitions[3].Width = new(narrow ? 48 : 68);
            Columns.ColumnSpacing = narrow ? 8 : 18;
        };
    }
    private void OnPlay(object? sender, RoutedEventArgs args) => PlayRequested?.Invoke(this, EventArgs.Empty);
    private void OnFavorite(object? sender, RoutedEventArgs args) => args.Handled = true;
    private void OnMenuPlay(object? sender, RoutedEventArgs args) => PlayRequested?.Invoke(this, EventArgs.Empty);
    private void OnMenuNext(object? sender, RoutedEventArgs args) => NextRequested?.Invoke(this, EventArgs.Empty);
    private void OnMenuAdd(object? sender, RoutedEventArgs args) => AddRequested?.Invoke(this, EventArgs.Empty);
}
