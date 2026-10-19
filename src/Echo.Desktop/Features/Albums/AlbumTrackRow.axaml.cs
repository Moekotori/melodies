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
        SizeChanged += (_, _) => UpdateColumns();
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        PlayButton.Classes.Set("album-page", Classes.Contains("album-page"));
        FavoriteButton.Classes.Set("album-page", Classes.Contains("album-page"));
        UpdateColumns();
    }
    private void UpdateColumns()
    {
        var albumPage = Classes.Contains("album-page");
        var width = TopLevel.GetTopLevel(this)?.ClientSize.Width ?? Bounds.Width;
        var narrow = albumPage ? width <= 980 || Bounds.Width < 720 : Bounds.Width < 720;
        Signal.IsVisible = !narrow;
        Columns.ColumnDefinitions[0].Width = new(narrow ? albumPage ? 38 : 28 : albumPage && DataContext is AlbumTrackItem item && item.Number.Contains('-') ? 58 : 46);
        Columns.ColumnDefinitions[2].Width = new(narrow ? 0 : 230);
        Columns.ColumnDefinitions[3].Width = new(narrow ? albumPage ? 56 : 48 : 68);
        Columns.ColumnSpacing = narrow ? albumPage ? 12 : 8 : 18;
    }
    private void OnPlay(object? sender, RoutedEventArgs args) => PlayRequested?.Invoke(this, EventArgs.Empty);
    private void OnFavorite(object? sender, RoutedEventArgs args)
    {
        // Handling the child click also suppresses Button's command dispatch. Execute it once
        // here, then stop the event reaching the row's play action.
        args.Handled = true;
        if (sender is Button { Command: { } command } button && command.CanExecute(button.CommandParameter))
            command.Execute(button.CommandParameter);
    }
    private void OnMenuPlay(object? sender, RoutedEventArgs args) => PlayRequested?.Invoke(this, EventArgs.Empty);
    private void OnMenuNext(object? sender, RoutedEventArgs args) => NextRequested?.Invoke(this, EventArgs.Empty);
    private void OnMenuAdd(object? sender, RoutedEventArgs args) => AddRequested?.Invoke(this, EventArgs.Empty);
}
