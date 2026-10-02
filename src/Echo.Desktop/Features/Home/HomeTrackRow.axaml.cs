using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Features.Home;

public partial class HomeTrackRow : UserControl
{
    private HomeViewModel? model;
    private AlbumArtworkCache.Lease? lease;
    private CancellationTokenSource? request;
    public HomeTrackRow()
    {
        InitializeComponent(); AttachedToVisualTree += (_, _) => _ = AttachAsync();
        DetachedFromVisualTree += (_, _) => { request?.Cancel(); request?.Dispose(); request = null; Cover.Source = null; lease?.Dispose(); lease = null; model = null; };
    }
    private async Task AttachAsync()
    {
        model = this.FindAncestorOfType<HomeView>()?.Model;
        if (DataContext is not ListeningTrack item || model is null) return;
        PlayCount.Text = model.Shell.Text.Format("home.plays", item.Plays);
        if (model.Shell.AlbumArtwork is not { } cache) return;
        request = new(); var current = request;
        var acquired = await cache.AcquireAsync(item.Track.Path, current.Token, 96);
        if (current.IsCancellationRequested) { acquired?.Dispose(); return; }
        lease = acquired; Cover.Source = lease?.Image;
    }
    private void OnPlay(object? sender, RoutedEventArgs args)
    { if (DataContext is ListeningTrack item) model?.PlayTrackCommand.Execute(item); }
}
