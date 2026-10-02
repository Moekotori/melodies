using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Features.Folders;

public partial class FoldersView
{
    private CancellationTokenSource? _artwork;
    private readonly List<AlbumArtworkCache.Lease> _leases = [];
    private async Task LoadArtworkAsync()
    {
        ReleaseArtwork();
        if (_closed || !_visible || Model.Tracks.Count == 0) return;
        using var request = new CancellationTokenSource(); _artwork = request;
        try
        {
            var samples = Model.Tracks.DistinctBy(track => track.Row.Album).Take(3).Select(track => track.Row.Path).ToArray();
            Avalonia.Controls.Image[] images = [CoverFront, CoverMiddle, CoverBack];
            for (var i = 0; i < samples.Length; i++)
            {
                var lease = await _shell.AlbumArtwork!.AcquireAsync(samples[i], request.Token);
                if (request.IsCancellationRequested || _closed) { lease?.Dispose(); return; }
                if (lease is not null) { _leases.Add(lease); images[i].Source = lease.Image; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Artwork is optional; the native folder mark remains visible. */ }
        finally { if (ReferenceEquals(_artwork, request)) _artwork = null; }
    }
    private void ReleaseArtwork()
    {
        _artwork?.Cancel(); _artwork = null;
        CoverFront.Source = null; CoverMiddle.Source = null; CoverBack.Source = null;
        foreach (var lease in _leases) lease.Dispose(); _leases.Clear();
    }
}
