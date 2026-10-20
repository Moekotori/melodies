using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Albums;

// Finite scroll viewport plus a virtual extent. Only visible rows and one row on either side exist.
public sealed class AlbumWallViewport : UserControl, IDisposable
{
    private const double RightPadding = 8;
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Canvas _canvas = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
    private readonly Dictionary<int, AlbumCard> _cards = [];
    private readonly AlbumsViewModel _model;
    private readonly AlbumArtworkCache _artwork;
    private bool _queued, _closed, _suspended;
    private int _columns;
    private double _layoutWidth, _layoutHeight, _rowHeight;
    private double? _pendingScrollOffset;
    private TopLevel? _topLevel;
    public int FixedColumns { get; set; }
    public int RealizedCards => _cards.Count;
    public double ScrollOffset => _scroll.Offset.Y;
    public event Action<AlbumRow>? OpenAlbum;

    public AlbumWallViewport(AlbumsViewModel model, AlbumArtworkCache artwork)
    {
        _model = model; _artwork = artwork;
        _scroll.Content = _canvas; Content = _scroll;
        _scroll.ScrollChanged += OnScrollChanged;
        SizeChanged += (_, _) => RequestLayout();
        _model.Changed += OnModelChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null) _topLevel.ScalingChanged += OnScalingChanged;
        RequestLayout();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachScaling();
        base.OnDetachedFromVisualTree(e);
    }
    private void OnScalingChanged(object? sender, EventArgs e) => RequestLayout();
    private void DetachScaling()
    {
        if (_topLevel is not null) _topLevel.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
    }

    private void OnModelChanged(bool reset)
    {
        if (reset) { _pendingScrollOffset = null; _rowHeight = 0; _scroll.Offset = default; }
        RequestLayout();
    }
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y != 0 && _pendingScrollOffset is { } offset)
        { _pendingScrollOffset = null; _scroll.Offset = new(0, offset); }
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
        var compact = width <= 1200 || height <= 560;
        var columnGap = compact ? 16 : 24;
        var availableWidth = Math.Max(1, width - RightPadding);
        var targetColumnWidth = compact ? 128 : 180;
        var columnCount = (availableWidth + columnGap) / (targetColumnWidth + columnGap);
        var readableColumns = Math.Clamp((int)((availableWidth + columnGap) / (96 + columnGap)), 1, 8);
        var previousColumns = _columns;
        _columns = FixedColumns > 0 ? Math.Clamp(FixedColumns, 1, readableColumns)
            : Math.Clamp((int)(compact ? Math.Round(columnCount) : Math.Floor(columnCount)), 1, 32);
        var size = Math.Max(1, (availableWidth - (_columns - 1) * columnGap) / _columns);
        var rowHeight = Math.Ceiling(size + (compact ? 72 : 84));
        double? anchor = null;
        if (_rowHeight > 0 && (width != _layoutWidth || height != _layoutHeight))
        {
            var firstIndex = (int)(_scroll.Offset.Y / _rowHeight) * previousColumns;
            anchor = firstIndex / _columns * rowHeight;
        }
        _layoutWidth = width; _layoutHeight = height; _rowHeight = rowHeight;
        var rows = (_model.Total + _columns - 1) / _columns;
        _canvas.Width = width;
        _canvas.Height = Math.Max(1, rows * rowHeight);
        if (anchor is { } target)
        {
            target = Math.Clamp(target, 0, Math.Max(0, _canvas.Height - height));
            // A smaller window may need a larger extent before its new offset can be accepted.
            _pendingScrollOffset = target > Math.Max(0, _scroll.Extent.Height - height) ? target : null;
            _scroll.Offset = new(0, target);
        }
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
            card.SetSize(size, _topLevel?.RenderScaling ?? 1);
            card.SetOrder(index);
            Canvas.SetLeft(card, index % _columns * (size + columnGap));
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
        _closed = true; DetachScaling(); _model.Changed -= OnModelChanged;
        foreach (var card in _cards.Values) card.Dispose();
        _cards.Clear(); _canvas.Children.Clear(); OpenAlbum = null;
    }
}
