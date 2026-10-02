using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

namespace Echo.Desktop.Bootstrap;

// Lazy desktop lifetime: the same database instance owns reads and every imported scan.
internal sealed class DesktopLibrary(string path) : IAlbumLibrary, IArtistLibrary, IAlbumPersonalState, IPlayerTrackLibrary, IAsyncDisposable
{
    private readonly object _gate = new();
    private Task<LibraryDatabase>? _opening;
    private Task<ScanReport>? _scan;
    private bool _closed;

    private Task<LibraryDatabase> Database()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            return _opening ??= Task.Run(() => LibraryDatabase.OpenAsync(path));
        }
    }

    public async Task<AlbumPage> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetAlbumsAsync(query, cancellation).ConfigureAwait(false);

    public async Task<ArtistPage> GetArtistsAsync(ArtistQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetArtistsAsync(query, cancellation).ConfigureAwait(false);
    public async Task<TrackPage> GetArtistTracksAsync(ArtistTrackQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetArtistTracksAsync(query, cancellation).ConfigureAwait(false);
    public async Task<AlbumPage> GetArtistAlbumsAsync(string id, ArtistGrouping grouping, AlbumQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetArtistAlbumsAsync(id, grouping, query, cancellation).ConfigureAwait(false);

    public async Task<TrackPage> GetAlbumTracksAsync(AlbumTrackQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetAlbumTracksAsync(query, cancellation).ConfigureAwait(false);

    public async Task SetAlbumStateAsync(string id, bool favorite, int rating, CancellationToken cancellation)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).SetAlbumStateAsync(id, favorite, rating, cancellation).ConfigureAwait(false);
    public async Task SetTrackFavoriteAsync(string id, bool favorite, CancellationToken cancellation)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).SetTrackFavoriteAsync(id, favorite, cancellation).ConfigureAwait(false);

    public async Task<TrackRow?> FindTrackByPathAsync(string path, CancellationToken cancellation)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).FindTrackByPathAsync(path, cancellation).ConfigureAwait(false);

    public Task<ScanReport> ImportAsync(string folder, IProgress<ScanProgress> progress, CancellationToken cancellation)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_scan is { IsCompleted: false }) throw new InvalidOperationException("A scan is already active.");
            return _scan = RunImportAsync(folder, progress, cancellation);
        }
    }

    private async Task<ScanReport> RunImportAsync(string folder, IProgress<ScanProgress> progress, CancellationToken cancellation)
    {
        var database = await Database().WaitAsync(cancellation).ConfigureAwait(false);
        var native = Environment.GetEnvironmentVariable("ECHO_METADATA_SCANNER");
        var scan = new ScanCoordinator(database, new DirectoryDiscovery(), () => new LocalMetadataReader(native));
        return await scan.ScanAsync(new(folder), progress, cancellation).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Task<LibraryDatabase>? opening;
        Task<ScanReport>? scan;
        lock (_gate) { _closed = true; opening = _opening; scan = _scan; }
        if (scan is not null) { try { await scan; } catch (Exception) { } }
        if (opening is not null)
        {
            try { await (await opening).DisposeAsync(); }
            catch (Exception) { /* Initialization errors are reported by the page. */ }
        }
    }
}
