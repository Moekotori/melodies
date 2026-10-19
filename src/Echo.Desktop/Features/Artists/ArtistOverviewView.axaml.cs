using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Artists;

public partial class ArtistOverviewView : UserControl
{
    public event Action<TrackRow>? PlayTrack;
    public event Action<ArtistRow>? OpenArtist;
    public event Action<AlbumRow>? OpenAlbum;
    public ArtistOverviewView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 720;
            Introduction.ColumnDefinitions[1].Width = new(narrow ? 0 : 250);
            Grid.SetRow(OnlineFacts, narrow ? 1 : 0); Grid.SetColumn(OnlineFacts, narrow ? 0 : 1);
            ListeningFacts.Columns = Bounds.Width < 560 ? 2 : 4;
            ListeningFacts.Rows = Bounds.Width < 560 ? 2 : 1;
        };
    }
    private void OnTrack(object? sender, RoutedEventArgs args) { if (sender is Button { Tag: TrackRow row }) PlayTrack?.Invoke(row); }
    private void OnArtist(object? sender, RoutedEventArgs args) { if (sender is Button { Tag: ArtistRow row }) OpenArtist?.Invoke(row); }
    private void OnAlbum(object? sender, RoutedEventArgs args) { if (sender is Button { Tag: AlbumRow row }) OpenAlbum?.Invoke(row); }
    private async void OnExternal(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string url } || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) return;
        try { if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(uri); }
        catch (Exception) { if (DataContext is ArtistOverviewViewModel model) model.OnlineNotice = "无法打开链接，请检查系统浏览器设置。"; }
    }
}
