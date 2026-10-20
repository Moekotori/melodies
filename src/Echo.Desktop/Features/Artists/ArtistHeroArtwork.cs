using System.ComponentModel;
using Avalonia.Controls;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Features.Artists;

// Matches ECHO's album-cover -> artist portrait -> matched biography image fallback.
internal sealed class ArtistHeroArtwork : IDisposable
{
    private readonly AlbumArtworkCache cache;
    private readonly ArtistOverviewViewModel overview;
    private readonly Image target;
    private readonly CancellationTokenSource lifetime = new();
    private AlbumArtworkCache.Lease? lease;
    private bool localComplete, closed;
    private string? attemptedOnline;
    private int generation;
    internal ArtistHeroArtwork(ArtistRow artist, AlbumArtworkCache cache, ArtistOverviewViewModel overview, Image target)
    {
        this.cache = cache; this.overview = overview; this.target = target;
        overview.PropertyChanged += OnInformation;
        _ = LoadLocalAsync(artist);
    }
    private async Task LoadLocalAsync(ArtistRow artist)
    {
        var found = await cache.AcquireAsync(artist.SamplePath, lifetime.Token, 960);
        if (closed) { found?.Dispose(); return; }
        if (found?.Image is null)
        {
            found?.Dispose(); found = await cache.AcquireArtistAsync(artist.PortraitPath ?? artist.SamplePath, lifetime.Token, 960);
        }
        if (closed) { found?.Dispose(); return; }
        if (found?.Image is not null) { lease = found; target.Source = found.Image; }
        else found?.Dispose();
        localComplete = true;
        if (lease is null) await LoadOnlineAsync();
    }
    private void OnInformation(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(ArtistOverviewViewModel.Information) && localComplete && lease is null) _ = LoadOnlineAsync(); }
    private async Task LoadOnlineAsync()
    {
        var reference = overview.Information?.Biography?.ArtworkReference;
        if (closed || string.IsNullOrEmpty(reference) || reference == attemptedOnline) return;
        attemptedOnline = reference; var current = ++generation;
        var found = await cache.AcquireArtistAsync("online:" + reference, lifetime.Token, 960);
        if (closed || current != generation) { found?.Dispose(); return; }
        if (found?.Image is not null) { lease?.Dispose(); lease = found; target.Source = found.Image; }
        else found?.Dispose();
    }
    public void Dispose()
    {
        if (closed) return; closed = true; lifetime.Cancel(); overview.PropertyChanged -= OnInformation;
        target.Source = null; lease?.Dispose(); lease = null;
        lifetime.Dispose();
    }
}
