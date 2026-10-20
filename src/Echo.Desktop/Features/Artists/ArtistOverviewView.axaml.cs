using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Artists;

public partial class ArtistOverviewView : UserControl
{
    public event Action<TrackRow>? PlayTrack;
    public event Action<TrackRow>? PlayNext;
    public event Action<TrackRow>? AddToQueue;
    public event Action<ArtistRow>? OpenArtist;
    public event Action<AlbumRow>? OpenAlbum;
    public ArtistOverviewView()
    {
        InitializeComponent();
        BiographyText.SizeChanged += (_, _) => UpdateBiographyToggle();
        BiographyText.PropertyChanged += (_, args) => { if (args.Property == TextBlock.TextProperty) UpdateBiographyToggle(); };
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 820;
            Introduction.ColumnDefinitions[1].Width = new(narrow ? 0 : 360);
            Introduction.ColumnSpacing = narrow ? 0 : 32;
            Grid.SetRow(OnlineFacts, narrow ? 1 : 0); Grid.SetColumn(OnlineFacts, narrow ? 0 : 1);
            OnlineFacts.BorderThickness = narrow ? new(0, 1, 0, 0) : new(1, 0, 0, 0);
            OnlineFacts.Padding = narrow ? new(0, 24, 0, 0) : new(24, 0, 0, 0);
            if (LibraryFactsList.ItemsPanelRoot is Avalonia.Controls.Primitives.UniformGrid facts) facts.Columns = Bounds.Width < 580 ? 2 : narrow ? 3 : 4;
            ListeningBody.ColumnDefinitions[1].Width = narrow ? new(0) : new(1, GridUnitType.Star);
            ListeningBody.ColumnSpacing = narrow ? 0 : 36;
            Grid.SetRow(TopTracksPanel, narrow ? 1 : 0); Grid.SetColumn(TopTracksPanel, narrow ? 0 : 1);
            ListeningFacts.Columns = narrow ? 2 : 3; ListeningFacts.Rows = narrow ? 2 : 1;
            if (CollaborationsList.ItemsPanelRoot is Avalonia.Controls.Primitives.UniformGrid grid) grid.Columns = narrow ? 1 : 2;
        };
    }
    private void UpdateBiographyToggle() => BiographyToggle.IsVisible = DataContext is ArtistOverviewViewModel { HasBiography: true } model
        && (model.BiographyExpanded || BiographyText.TextLayout.Height > BiographyText.Bounds.Height + 1);
    private void OnTrack(object? sender, RoutedEventArgs args) { if (sender is Button { Tag: TrackRow row }) PlayTrack?.Invoke(row); }
    private void OnArtist(object? sender, RoutedEventArgs args) { if (sender is Button { Tag: ArtistRow row }) OpenArtist?.Invoke(row); }
    private void OnAlbum(object? sender, RoutedEventArgs args) { if (sender is Button { Tag: AlbumRow row }) OpenAlbum?.Invoke(row); }
    private async void OnRelation(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: ArtistOnlineRelation relation }) return;
        if (relation.LocalArtist is { } local) OpenArtist?.Invoke(local);
        else await ArtistExternalLink.OpenAsync(this, relation.Url, DataContext as ArtistOverviewViewModel);
    }
    private void OnCardPlay(TrackRow track) => PlayTrack?.Invoke(track);
    private void OnCardNext(TrackRow track) => PlayNext?.Invoke(track);
    private void OnCardAdd(TrackRow track) => AddToQueue?.Invoke(track);
    private async void OnExternal(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string url }) await ArtistExternalLink.OpenAsync(this, url, DataContext as ArtistOverviewViewModel);
    }
}
