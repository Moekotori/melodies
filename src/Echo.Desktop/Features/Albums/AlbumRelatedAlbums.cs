using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumDetailView
{
    private readonly List<AlbumArtworkCache.Lease> _relatedLeases = [];
    private async Task LoadRelatedAlbumsAsync(CancellationToken token, int generation)
    {
        ClearRelatedAlbums();
        if (_library is null || _artwork is null || string.IsNullOrWhiteSpace(_model.Album.Artist)) return;
        var artist = _model.Album.Artist;
        var page = await _library.GetAlbumsAsync(new(Search: artist[..Math.Min(128, artist.Length)]), token);
        if (_closed || generation != _panelGeneration) return;
        foreach (var album in page.Items.Where(item => item.Id != _model.Album.Id && item.Artist.Equals(artist, StringComparison.OrdinalIgnoreCase)).Take(6))
        {
            var lease = await _artwork.AcquireAsync(album.SamplePath, token, maxEdge: 192);
            if (_closed || generation != _panelGeneration) { lease?.Dispose(); return; }
            if (lease is not null) _relatedLeases.Add(lease);
            var stack = new StackPanel { Spacing = 7, Width = 140 };
            stack.Children.Add(new Image { Source = lease?.Image, Width = 140, Height = 140, Stretch = Stretch.UniformToFill });
            var label = new TextBlock { Text = album.Title, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("EchoHeading")); stack.Children.Add(label);
            var year = new TextBlock { Text = album.Year?.ToString() ?? $"{album.TrackCount} 首歌", FontSize = 11 };
            year.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("EchoMuted")); stack.Children.Add(year);
            var card = new Button { Content = stack, Padding = new(0), Background = Brushes.Transparent, Margin = new(0, 0, 18, 12), VerticalAlignment = VerticalAlignment.Top };
            card.Click += (_, args) => { args.Handled = true; AlbumRequested?.Invoke(album); };
            ToolTip.SetTip(card, album.Title); RelatedAlbums.Children.Add(card);
        }
        RelatedPanel.IsVisible = _model.IsInformation && RelatedAlbums.Children.Count > 0;
    }
    private void ClearRelatedAlbums()
    {
        RelatedAlbums.Children.Clear(); RelatedPanel.IsVisible = false;
        foreach (var lease in _relatedLeases) lease.Dispose(); _relatedLeases.Clear();
    }
}
