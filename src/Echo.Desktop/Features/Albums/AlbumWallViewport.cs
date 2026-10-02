using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Albums;

// Finite scroll viewport plus a virtual extent. Only visible rows and one row on either side exist.
public sealed class AlbumWallViewport : UserControl, IDisposable
{
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Canvas _canvas = new();
    private readonly Dictionary<int, AlbumCard> _cards = [];
    private readonly AlbumsViewModel _model;
    private readonly AlbumArtworkCache _artwork;
    private bool _queued, _closed, _suspended;
    private int _columns;
    public int FixedColumns { get; set; }
    public int RealizedCards => _cards.Count;
    public double ScrollOffset => _scroll.Offset.Y;
    public event Action<AlbumRow>? OpenAlbum;

    public AlbumWallViewport(AlbumsViewModel model, AlbumArtworkCache artwork)
    {
        _model = model; _artwork = artwork;
        _scroll.Content = _canvas; Content = _scroll;
        _scroll.ScrollChanged += (_, _) => RequestLayout();
        SizeChanged += (_, _) => RequestLayout();
        _model.Changed += OnModelChanged;
    }

    private void OnModelChanged(bool reset)
    {
        if (reset) _scroll.Offset = default;
        RequestLayout();
    }
    public void ScrollTo(double y) => _scroll.Offset = new(0, y);
    public void RequestLayout()
    {
        if (_closed || _queued) return;
        _queued = true;
        Dispatcher.UIThread.Post(() => { _queued = false; if (!_closed) RenderRange(); }, DispatcherPriority.Background);
    }
    private void RenderRange()
    {
        if (_suspended) return;
        var width = _scroll.Viewport.Width > 0 ? _scroll.Viewport.Width : Bounds.Width;
        var height = _scroll.Viewport.Height > 0 ? _scroll.Viewport.Height : Bounds.Height;
        if (width < 1 || height < 1) return;
        _columns = FixedColumns > 0 ? Math.Clamp(FixedColumns, 1, 8) : Math.Clamp((int)((width + 20) / 180), 1, 8);
        var size = Math.Max(1, (width - (_columns - 1) * 20) / _columns);
        var rowHeight = size + 88;
        var rows = (_model.Total + _columns - 1) / _columns;
        _canvas.Width = width;
        _canvas.Height = Math.Max(1, rows * rowHeight);
        var firstRow = Math.Max(0, (int)(_scroll.Offset.Y / rowHeight) - 1);
        var lastRow = Math.Min(rows, (int)Math.Ceiling((_scroll.Offset.Y + height) / rowHeight) + 1);
        var first = firstRow * _columns;
        var last = Math.Min(_model.Total, lastRow * _columns);
        foreach (var index in _cards.Keys.Where(index => index < first || index >= last).ToArray())
        { var card = _cards[index]; _cards.Remove(index); _canvas.Children.Remove(card); card.Dispose(); }
        for (var index = first; index < last; index++)
        {
            if (!_cards.TryGetValue(index, out var card))
            {
                card = new AlbumCard();
                card.Open += album => OpenAlbum?.Invoke(album);
                _cards.Add(index, card); _canvas.Children.Add(card);
            }
            card.SetSize(size);
            card.SetOrder(index);
            Canvas.SetLeft(card, index % _columns * (size + 20));
            Canvas.SetTop(card, index / _columns * rowHeight);
            card.Bind(_model.At(index), _artwork);
        }
        if (last > first) _ = _model.LoadRangeAsync(first, last - first);
    }

    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        if (suspended)
        {
            foreach (var card in _cards.Values) card.Dispose();
            _cards.Clear(); _canvas.Children.Clear();
        }
        else RequestLayout();
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _model.Changed -= OnModelChanged;
        foreach (var card in _cards.Values) card.Dispose();
        _cards.Clear(); _canvas.Children.Clear(); OpenAlbum = null;
    }
}
