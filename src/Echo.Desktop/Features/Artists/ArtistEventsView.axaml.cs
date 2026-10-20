using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Artists;

public partial class ArtistEventsView : UserControl
{
    public ArtistEventsView() => InitializeComponent();
    private async void OnExternal(object? sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string url }) await ArtistExternalLink.OpenAsync(this, url, DataContext as ArtistOverviewViewModel); }
}
