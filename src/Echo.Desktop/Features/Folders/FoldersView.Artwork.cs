using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Features.Folders;

public partial class FoldersView
{
    private CancellationTokenSource? _artwork;
    private AlbumArtworkCache.Lease? _coverLease;
    private string[] _coverSamples = [];
    private async Task LoadArtworkAsync()
    {
        if (_closed || !_visible || _artwork is not null || _coverLease is not null) return;
        if (_coverSamples.Length == 0)
            _coverSamples = Model.Tracks.Take(1).Select(track => track.Row.Path).ToArray();
        if (_coverSamples.Length == 0) return;
        using var request = new CancellationTokenSource(); _artwork = request;
        try
        {
            var lease = await _shell.AlbumArtwork!.AcquireAsync(_coverSamples[0], request.Token, 320);
            if (request.IsCancellationRequested || _closed) { lease?.Dispose(); return; }
            _coverLease = lease; CoverFront.Source = lease?.Image;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* The shared placeholder remains visible when artwork is unavailable. */ }
        finally { if (ReferenceEquals(_artwork, request)) _artwork = null; }
    }
    private void ReleaseArtwork()
    {
        _artwork?.Cancel(); _artwork = null;
        CoverFront.Source = null; _coverLease?.Dispose(); _coverLease = null;
    }
}
