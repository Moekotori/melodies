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
            var rail = new UniformGrid { Columns = 2 };
            foreach (var album in albums.Take(7))
            {
                var card = new HomeAlbumCard(album, shell, open, true) { Margin = new(0, 0, 16, 14) };
                cards.Add(card); rail.Children.Add(card);
            }
            SizeChanged += (_, _) => rail.Columns = Bounds.Width < 560 ? 1 : 2;
            Content = rail;
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
