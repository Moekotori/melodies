using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Features.Genres;

public sealed class GenreWallViewport : UserControl, IDisposable
{
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Canvas _canvas = new();
    private readonly Dictionary<int, GenreCard> _cards = [];
    private readonly Stack<GenreCard> _pool = new(24);
    private readonly GenresViewModel _model;
    private readonly AlbumArtworkCache _artwork;
    private bool _closed, _queued, _suspended;
    private int _columns = 1;
    private int? _pendingFocus;
    public int RealizedCards => _cards.Count;
    public event Action<GenreRow>? OpenGenre;
    public GenreWallViewport(GenresViewModel model, AlbumArtworkCache artwork)
    {
        _model = model; _artwork = artwork; Content = _scroll; _scroll.Content = _canvas;
        _scroll.ScrollChanged += (_, _) => RequestLayout(); SizeChanged += (_, _) => RequestLayout();
        _model.Changed += OnChanged;
    }
    private void OnChanged(bool reset) { if (reset) { _scroll.Offset = default; _pendingFocus = null; } RequestLayout(); }
    public void ScrollTo(double y) => _scroll.Offset = new(0, y);
    public void RequestLayout()
    {
        if (_closed || _queued) return; _queued = true;
        Dispatcher.UIThread.Post(() => { _queued = false; if (!_closed && !_suspended) RenderRange(); }, DispatcherPriority.Background);
    }
    private void RenderRange()
    {
        var width = _scroll.Viewport.Width > 0 ? _scroll.Viewport.Width : Bounds.Width;
        var height = _scroll.Viewport.Height > 0 ? _scroll.Viewport.Height : Bounds.Height;
        if (width < 1 || height < 1) return;
        width = Math.Max(1, width - 10);
        var columns = Math.Clamp((int)((width + 22) / 150), 1, 12);
        _columns = columns;
        var cardWidth = (width - (columns - 1) * 22) / columns;
        const double rowHeight = 216;
        var rows = (_model.Total + columns - 1) / columns;
        _canvas.Width = width; _canvas.Height = Math.Max(1, rows * rowHeight + 18);
        var first = Math.Max(0, (int)(_scroll.Offset.Y / rowHeight) - 1) * columns;
        var last = Math.Min(_model.Total, ((int)Math.Ceiling((_scroll.Offset.Y + height) / rowHeight) + 1) * columns);
        foreach (var index in _cards.Keys.Where(index => index < first || index >= last).ToArray())
        {
            var card = _cards[index]; _cards.Remove(index); _canvas.Children.Remove(card); card.Reset();
            if (_pool.Count < 24) _pool.Push(card); else card.Dispose();
        }
        for (var index = first; index < last; index++)
        {
            if (!_cards.TryGetValue(index, out var card))
            {
                if (_pool.TryPop(out var pooled)) card = pooled;
                else { card = new(); card.Open += artist => OpenGenre?.Invoke(artist); card.MoveFocusRequested += MoveFocus; }
                _cards[index] = card; _canvas.Children.Add(card);
            }
            card.Width = cardWidth; Canvas.SetLeft(card, index % columns * (cardWidth + 22)); Canvas.SetTop(card, index / columns * rowHeight + 4);
            card.Text = _model.Text; card.Bind(_model.At(index), _artwork, index);
        }
        if (last > first) _ = _model.LoadRangeAsync(first, last - first);
        if (_pendingFocus is { } focus && _cards.TryGetValue(focus, out var target) && target.FocusCard()) _pendingFocus = null;
    }
    private void MoveFocus(int index, Key key)
    {
        if (_model.Total == 0) return;
        var next = Math.Clamp(key switch { Key.Left => index - 1, Key.Right => index + 1, Key.Up => index - _columns,
            Key.Down => index + _columns, Key.Home => 0, Key.End => _model.Total - 1, _ => index }, 0, _model.Total - 1);
        _pendingFocus = next;
        var y = next / _columns * 216;
        if (y < _scroll.Offset.Y) ScrollTo(y);
        else if (y + 186 > _scroll.Offset.Y + _scroll.Viewport.Height) ScrollTo(y + 186 - _scroll.Viewport.Height);
        RequestLayout();
    }
    public void SetSuspended(bool value)
    {
        _suspended = value;
        if (value) ClearCards(); else RequestLayout();
    }
    public void ReloadArtwork() { SetSuspended(true); _artwork.ClearUnused(); SetSuspended(false); }
    private void ClearCards()
    { foreach (var card in _cards.Values) card.Dispose(); _cards.Clear(); _canvas.Children.Clear(); while (_pool.TryPop(out var card)) card.Dispose(); }
    public void Dispose() { if (_closed) return; _closed = true; _model.Changed -= OnChanged; ClearCards(); OpenGenre = null; }
}
