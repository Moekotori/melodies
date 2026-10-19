using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Playlists;

// A bounded, virtualized playlist wall. Cards acquire artwork only while attached.
public sealed class PlaylistBrowser : ListBox, IDisposable
{
    protected override Type StyleKeyOverride => typeof(ListBox);
    private readonly PlaylistsViewModel model;
    private readonly AlbumArtworkCache? artwork;
    private readonly LocalizationService text;
    private sealed record WallEntry(PlaylistRow? Playlist = null, string? Favorite = null);
    private int columns;
    private double cardSize, layoutWidth;
    private bool suspended, closed;
    public PlaylistBrowser(PlaylistsViewModel model, AlbumArtworkCache? artwork, LocalizationService text)
    {
        this.model = model; this.artwork = artwork; this.text = text;
        BorderThickness = new(0); Padding = new(0); Background = Avalonia.Media.Brushes.Transparent;
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        ItemTemplate = new FuncDataTemplate<IReadOnlyList<WallEntry>>((rows, _) =>
        {
            var grid = new Grid { ColumnSpacing = 24, Margin = new(0, 0, 42, 0) };
            if (rows is null) return grid;
            for (var i = 0; i < rows.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(cardSize)));
                Control tile;
                if (rows[i].Playlist is { } playlist)
                {
                    var playlistTile = new PlaylistTile(playlist, cardSize, artwork, text);
                    playlistTile.Open += model.OpenPlaylist; tile = playlistTile;
                }
                else tile = new PlaylistFavoriteTile(model, rows[i].Favorite!, cardSize);
                Grid.SetColumn(tile, i); grid.Children.Add(tile);
            }
            return grid;
        });
        model.PropertyChanged += Changed; text.Changed += Rebuild;
        SizeChanged += (_, _) =>
        { if (Math.Abs(Bounds.Width - layoutWidth) > .5) { layoutWidth = Bounds.Width; Rebuild(); } };
        Rebuild();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(PlaylistsViewModel.Playlists) or nameof(PlaylistsViewModel.IsBrowsing)
        or nameof(PlaylistsViewModel.LikedTracks) or nameof(PlaylistsViewModel.LikedAlbums)) Rebuild(); }
    private void Rebuild()
    {
        if (closed || suspended) return;
        var contentWidth = Math.Max(0, Bounds.Width - 54);
        columns = Math.Clamp((int)((contentWidth + 24) / 190), 1, 8);
        cardSize = Math.Max(80, (contentWidth - 24 * (columns - 1)) / columns);
        var entries = new List<WallEntry>(model.Playlists.Count + 2);
        if (string.IsNullOrWhiteSpace(model.PlaylistSearch) && model.IsFirstBrowsePage)
        { entries.Add(new(Favorite: "liked")); entries.Add(new(Favorite: "albums")); }
        entries.AddRange(model.Playlists.Select(row => new WallEntry(row)));
        ItemsSource = entries.Chunk(columns).Select(chunk => (IReadOnlyList<WallEntry>)chunk).ToArray();
    }
    public void SetSuspended(bool value) { suspended = value; if (value) ItemsSource = null; else Rebuild(); }
    public void Dispose() { if (closed) return; closed = true; model.PropertyChanged -= Changed; text.Changed -= Rebuild; ItemsSource = null; }
}
