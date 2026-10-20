using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumCard : UserControl, IDisposable
{
    private AlbumRow? _album;
    private CancellationTokenSource? _request;
    private AlbumArtworkCache.Lease? _lease;
    private AlbumArtworkCache? _cache;
    private int _decodeEdge = 320, _requestedEdge;
    private bool _bound;
    public event Action<AlbumRow>? Open;
    public AlbumCard() { InitializeComponent(); }
    public void SetSize(double size, double renderScale = 1)
    {
        Width = size; CoverFrame.Width = size; CoverFrame.Height = size;
        // Wall thumbnails allow slight upscaling; keep high-DPI decoding and cache variants small.
        var pixels = size * renderScale * 0.8;
        _decodeEdge = pixels switch { <= 192 => 192, <= 256 => 256, _ => 320 };
        RefreshCover();
    }
    public void SetOrder(int index) => OpenButton.TabIndex = index;

    public void Bind(AlbumRow? album, AlbumArtworkCache cache)
    {
        if (_bound && _album == album) return;
        _bound = true;
        ReleaseCover();
        _album = album;
        _cache = cache;
        AlbumTitle.Text = album?.Title ?? "正在读取…";
        Artist.Text = album?.Artist ?? "";
        TrackCount.Text = album is null ? "" : $"{album.TrackCount} 首";
        OpenButton.IsEnabled = album is not null;
        AutomationProperties.SetName(OpenButton, album is null ? "正在读取专辑" : $"{album.Title}，{album.Artist}，{album.TrackCount} 首");
        ToolTip.SetTip(OpenButton, album is null ? null : $"{album.Title}\n{album.Artist}");
        RefreshCover();
    }

    private void RefreshCover()
    {
        if (_album is not { } album || _cache is not { } cache || _requestedEdge == _decodeEdge) return;
        _request?.Cancel();
        _requestedEdge = _decodeEdge;
        _request = new();
        _ = LoadCoverAsync(cache, album, _decodeEdge, _request);
    }

    private async Task LoadCoverAsync(AlbumArtworkCache cache, AlbumRow album, int edge, CancellationTokenSource request)
    {
        try
        {
            var lease = await cache.AcquireAsync(album.SamplePath, request.Token, edge);
            if (request.IsCancellationRequested || _album != album || !ReferenceEquals(_request, request)) { lease?.Dispose(); return; }
            if (lease?.Image is null) { lease?.Dispose(); _requestedEdge = 0; return; }
            Cover.Source = lease.Image;
            _lease?.Dispose();
            _lease = lease;
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
    }

    private void OnOpen(object? sender, RoutedEventArgs args) { if (_album is { } album) Open?.Invoke(album); }
    private void ReleaseCover()
    { _request?.Cancel(); _request = null; _requestedEdge = 0; Cover.Source = null; _lease?.Dispose(); _lease = null; }
    public void Dispose() { ReleaseCover(); _cache = null; _album = null; Open = null; }
}
