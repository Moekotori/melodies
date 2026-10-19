using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Folders;

public partial class FolderCard : UserControl, IDisposable
{
    private CancellationTokenSource? _request;
    private AlbumArtworkCache.Lease? _lease;
    public FolderNode Node { get; }
    public event Action<FolderNode>? Open;
    public FolderCard() { Node = null!; InitializeComponent(); }
    public FolderCard(FolderNode node, AlbumArtworkCache? cache, LocalizationService text) : this()
    {
        Node = node; FolderName.Text = node.Name;
        TrackCount.Text = text.Format("playlists.tracksCount", node.Row.TrackCount);
        AutomationProperties.SetName(OpenButton, node.Name); ToolTip.SetTip(OpenButton, node.Row.Path);
        if (cache is not null && node.Row.SamplePath is { } sample)
        { _request = new(); _ = LoadCoverAsync(cache, sample, _request); }
    }
    public void SetSize(double width)
    { Width = width; ArtworkFrame.Width = ArtworkFrame.Height = width - 6; }
    public void SetOrder(int order) => OpenButton.TabIndex = order;
    private async Task LoadCoverAsync(AlbumArtworkCache cache, string sample, CancellationTokenSource request)
    {
        try
        {
            var lease = await cache.AcquireAsync(sample, request.Token, 240);
            if (request.IsCancellationRequested) { lease?.Dispose(); return; }
            _lease = lease; Cover.Source = lease?.Image;
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
    }
    private void OnOpen(object? sender, RoutedEventArgs args) => Open?.Invoke(Node);
    public void Dispose() { _request?.Cancel(); _request = null; Cover.Source = null; _lease?.Dispose(); _lease = null; Open = null; }
}
