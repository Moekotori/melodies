using Avalonia.Controls;
using Avalonia.Input;
using Echo.Desktop.Controls;
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
            var stack = new StackPanel { Spacing = 9, Width = 160 };
            var sleeve = new Border { Width = 160, Height = 160, CornerRadius = new(8), ClipToBounds = true };
            sleeve.Bind(Border.BackgroundProperty, new DynamicResourceExtension("EchoPanelMuted"));
            sleeve.Child = lease?.Image is { } image
                ? new Image { Source = image, Stretch = Stretch.UniformToFill }
                : new GlyphIcon { Kind = "disc", Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(sleeve);
            var label = new TextBlock { Text = album.Title, FontSize = 13, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("EchoHeading")); stack.Children.Add(label);
            var year = new TextBlock { Text = album.Year?.ToString() ?? $"{album.TrackCount} 首歌", FontSize = 12 };
            year.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("EchoMuted")); stack.Children.Add(year);
            var card = new Button { Content = stack, Padding = new(0), Background = Brushes.Transparent, Margin = new(0, 0, 18, 12), VerticalAlignment = VerticalAlignment.Top, Cursor = new(StandardCursorType.Hand) };
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
