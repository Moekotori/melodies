using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Home;

public sealed class HomeAlbumShelf : UserControl, IDisposable
{
    private readonly List<HomeAlbumCard> cards = [];
    private bool closed;
    public HomeAlbumShelf(IReadOnlyList<AlbumRow> albums, ShellViewModel shell, Action<AlbumRow> open, bool compact = false)
    {
        if (compact)
        {
            var rail = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 20 };
            for (var i = 0; i < Math.Min(7, albums.Count); i += 2)
            {
                var column = new StackPanel { Width = 336, Spacing = 4 };
                for (var j = i; j < Math.Min(i + 2, albums.Count); j++)
                { var card = new HomeAlbumCard(albums[j], shell, open, true); cards.Add(card); column.Children.Add(card); }
                rail.Children.Add(column);
            }
            Content = new ScrollViewer { Content = rail, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 0, 8) };
        }
        else
        {
            var rail = new UniformGrid { Columns = 4, Rows = 1, Margin = new(-8, 0) };
            foreach (var album in albums.Take(4))
            { var card = new HomeAlbumCard(album, shell, open) { Margin = new(8, 0) }; cards.Add(card); rail.Children.Add(card); }
            SizeChanged += (_, _) =>
            {
                rail.Columns = Bounds.Width <= 600 ? 2 : 4;
                rail.Rows = rail.Columns == 2 ? 2 : 1;
            };
            Content = rail;
        }
        DetachedFromVisualTree += (_, _) => Dispose();
    }
    public void Dispose() { if (closed) return; closed = true; foreach (var card in cards) card.Dispose(); cards.Clear(); }
}
