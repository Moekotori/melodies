using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Folders;

// Realize only viewport rows plus one row on either side; leases leave with cards.
public sealed class FolderWallViewport : UserControl, IDisposable
{
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Canvas _canvas = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
    private readonly Dictionary<int, FolderCard> _cards = [];
    private readonly FolderWallState _state;
    private readonly AlbumArtworkCache? _artwork;
    private readonly LocalizationService _text;
    private bool _closed, _queued, _suspended;
    public event Action<FolderNode>? Open;
    public FolderWallViewport(FolderWallState state, AlbumArtworkCache? artwork, LocalizationService text)
    {
        _state = state; _artwork = artwork; _text = text;
        Content = _scroll; _scroll.Content = _canvas;
        _scroll.ScrollChanged += (_, _) => RequestLayout(); SizeChanged += (_, _) => RequestLayout();
        state.Changed += Reset; text.Changed += Reset;
    }
    private void Reset() { Clear(); _scroll.Offset = default; RequestLayout(); }
    private void RequestLayout()
    {
        if (_closed || _queued) return;
        _queued = true; Dispatcher.UIThread.Post(() => { _queued = false; if (!_closed && !_suspended) RenderRange(); }, DispatcherPriority.Background);
    }
    private void RenderRange()
    {
        var width = _scroll.Viewport.Width > 0 ? _scroll.Viewport.Width : Bounds.Width;
        var height = _scroll.Viewport.Height > 0 ? _scroll.Viewport.Height : Bounds.Height;
        if (width < 1 || height < 1) return;
        const int gap = 26;
        var columns = Math.Clamp((int)((width + gap) / 220), 1, 8);
        var size = (width - (columns - 1) * gap) / columns;
        var rowHeight = size + 76;
        var rows = (_state.Items.Count + columns - 1) / columns;
        _canvas.Width = width; _canvas.Height = rows * rowHeight;
        var first = Math.Max(0, (int)(_scroll.Offset.Y / rowHeight) - 1) * columns;
        var last = Math.Min(_state.Items.Count, ((int)Math.Ceiling((_scroll.Offset.Y + height) / rowHeight) + 1) * columns);
        foreach (var index in _cards.Keys.Where(index => index < first || index >= last).ToArray())
        { var card = _cards[index]; _cards.Remove(index); _canvas.Children.Remove(card); card.Dispose(); }
        for (var index = first; index < last; index++)
        {
            if (!_cards.TryGetValue(index, out var card))
            {
                card = new(_state.Items[index], _artwork, _text); card.Open += node => Open?.Invoke(node);
                _cards.Add(index, card); _canvas.Children.Add(card);
            }
            card.SetSize(size); card.SetOrder(index);
            Canvas.SetLeft(card, index % columns * (size + gap)); Canvas.SetTop(card, index / columns * rowHeight);
        }
    }
    private void Clear() { foreach (var card in _cards.Values) card.Dispose(); _cards.Clear(); _canvas.Children.Clear(); }
    public void SetSuspended(bool suspended) { _suspended = suspended; if (suspended) Clear(); else RequestLayout(); }
    public void Dispose() { _closed = true; _state.Changed -= Reset; _text.Changed -= Reset; Clear(); Open = null; }
}
