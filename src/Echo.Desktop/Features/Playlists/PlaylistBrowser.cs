using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Playlists;

// Virtualizes rows in both layouts. Cards acquire artwork only while attached.
public sealed class PlaylistBrowser : ListBox, IDisposable
{
    protected override Type StyleKeyOverride => typeof(ListBox);
    private readonly PlaylistsViewModel model;
    private readonly AlbumArtworkCache? artwork;
    private readonly LocalizationService text;
    private int columns;
    private double cardSize;
    private bool suspended, closed;
    public PlaylistBrowser(PlaylistsViewModel model, AlbumArtworkCache? artwork, LocalizationService text)
    {
        this.model = model; this.artwork = artwork; this.text = text;
        BorderThickness = new(0); Padding = new(0); Background = Avalonia.Media.Brushes.Transparent;
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        ItemTemplate = new FuncDataTemplate<IReadOnlyList<PlaylistRow>>((rows, _) =>
        {
            var grid = new Grid { ColumnSpacing = 18 };
            if (rows is null) return grid;
            for (var i = 0; i < rows.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(model.Cards ? new GridLength(cardSize) : new GridLength(1, GridUnitType.Star)));
                var tile = new PlaylistTile(rows[i], model.Cards, cardSize, artwork, text);
                tile.Open += model.OpenPlaylist; Grid.SetColumn(tile, i); grid.Children.Add(tile);
            }
            return grid;
        });
        model.PropertyChanged += Changed; text.Changed += Rebuild;
        SizeChanged += (_, _) => { if (model.Cards) Rebuild(); };
        Rebuild();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(PlaylistsViewModel.Playlists) or nameof(PlaylistsViewModel.Cards)) Rebuild(); }
    private void Rebuild()
    {
        if (closed || suspended) return;
        columns = model.Cards ? Math.Clamp((int)((Bounds.Width + 18) / 178), 1, 8) : 1;
        cardSize = Math.Max(80, (Bounds.Width - 18 * (columns - 1) - 12) / columns);
        ItemsSource = model.Playlists.Chunk(columns).Select(chunk => (IReadOnlyList<PlaylistRow>)chunk).ToArray();
    }
    public void SetSuspended(bool value) { suspended = value; if (value) ItemsSource = null; else Rebuild(); }
    public void Dispose() { if (closed) return; closed = true; model.PropertyChanged -= Changed; text.Changed -= Rebuild; ItemsSource = null; }
}
