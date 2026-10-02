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
    private bool _bound;
    public event Action<AlbumRow>? Open;
    public AlbumCard() { InitializeComponent(); }
    public void SetSize(double size) { Width = size; CoverFrame.Width = size; CoverFrame.Height = size; }
    public void SetOrder(int index) => OpenButton.TabIndex = index;

    public void Bind(AlbumRow? album, AlbumArtworkCache cache)
    {
        if (_bound && _album == album) return;
        _bound = true;
        ReleaseCover();
        _album = album;
        AlbumTitle.Text = album?.Title ?? "正在读取…";
        Artist.Text = album?.Artist ?? "";
        TrackCount.Text = album is null ? "" : $"{album.TrackCount} 首";
        OpenButton.IsEnabled = album is not null;
        AutomationProperties.SetName(OpenButton, album is null ? "正在读取专辑" : $"{album.Title}，{album.Artist}，{album.TrackCount} 首");
        ToolTip.SetTip(OpenButton, album is null ? null : $"{album.Title}\n{album.Artist}");
        if (album is null) return;
        _request = new();
        _ = LoadCoverAsync(cache, album, _request);
    }

    private async Task LoadCoverAsync(AlbumArtworkCache cache, AlbumRow album, CancellationTokenSource request)
    {
        try
        {
            var lease = await cache.AcquireAsync(album.SamplePath, request.Token);
            if (request.IsCancellationRequested || _album != album) { lease?.Dispose(); return; }
            _lease = lease;
            Cover.Source = lease?.Image;
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
    }

    private void OnOpen(object? sender, RoutedEventArgs args) { if (_album is { } album) Open?.Invoke(album); }
    private void ReleaseCover()
    { _request?.Cancel(); _request = null; Cover.Source = null; _lease?.Dispose(); _lease = null; }
    public void Dispose() { ReleaseCover(); _album = null; Open = null; }
}
