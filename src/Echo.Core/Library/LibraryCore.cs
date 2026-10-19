using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;
using Echo.Contracts.Tasks;

namespace Echo.Core.Library;

// Core lifetime: one lazy database owner serves all attached controls and imported scans.
public sealed partial class LibraryCore(string path, IBackgroundWorkAdmission? admission = null) : ITrackLibrary, IAlbumLibrary, IArtistLibrary, IGenreLibrary, IFolderLibrary, IAlbumPersonalState, IPlayerTrackLibrary, IHomeLibrary, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task<LibraryDatabase>? _opening;
    private Task<ScanReport>? _scan;
    private Task? _disposal;
    private bool _closed;

    public async Task<HomeDashboard> GetHomeAsync(DateOnly today, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetHomeAsync(today, cancellation).ConfigureAwait(false);
    public async Task<IReadOnlyList<TrackRow>> GetRandomTracksAsync(CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetRandomTracksAsync(cancellation).ConfigureAwait(false);
    public async Task<AlbumRow?> GetTrackAlbumAsync(string path, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetTrackAlbumAsync(path, cancellation).ConfigureAwait(false);
    public async Task RecordListeningAsync(ListeningEntry entry, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).RecordListeningAsync(entry, cancellation).ConfigureAwait(false);

    private Task<LibraryDatabase> Database()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            return _opening ??= Task.Run(async () =>
            {
                var database = await LibraryDatabase.OpenAsync(path, _shutdown.Token).ConfigureAwait(false);
                try
                {
                    var rules = await database.GetFolderRulesAsync(_shutdown.Token).ConfigureAwait(false);
                    foreach (var rule in rules.Where(rule => rule.Watch).Take(16))
                    {
                        try
                        {
                            lock (_gate)
                            {
                                _shutdown.Token.ThrowIfCancellationRequested();
                                _folderWatch ??= new(ScanFolderAsync, changed => FolderChanged?.Invoke(changed));
                            }
                            await _folderWatch.SetAsync(rule.Path, true, _shutdown.Token).ConfigureAwait(false);
                        }
                        catch (Exception) when (!_shutdown.IsCancellationRequested) { /* Missing watched roots remain available for explicit retry. */ }
                    }
                    return database;
                }
                catch
                {
                    await database.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            });
        }
    }

    public async Task<TrackPage> GetTracksAsync(TrackQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetTracksAsync(query, cancellation).ConfigureAwait(false);

    public async Task<AlbumPage> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetAlbumsAsync(query, cancellation).ConfigureAwait(false);

    public async Task<FolderPage> GetFoldersAsync(FolderQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetFoldersAsync(query, cancellation).ConfigureAwait(false);
    public async Task<FolderTrackPage> GetFolderTracksAsync(FolderTrackQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetFolderTracksAsync(query, cancellation).ConfigureAwait(false);

    public async Task<GenrePage> GetGenresAsync(GenreQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetGenresAsync(query, cancellation).ConfigureAwait(false);
    public async Task<TrackPage> GetGenreTracksAsync(GenreTrackQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetGenreTracksAsync(query, cancellation).ConfigureAwait(false);
    public async Task<AlbumPage> GetGenreAlbumsAsync(string id, AlbumQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetGenreAlbumsAsync(id, query, cancellation).ConfigureAwait(false);

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
            return _scan = Task.Run(() => RunImportAsync(folder, progress, cancellation));
        }
    }

    private async Task<ScanReport> RunImportAsync(string folder, IProgress<ScanProgress> progress, CancellationToken cancellation, FolderScanOptions? options = null)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _shutdown.Token);
        cancellation = lifetime.Token;
        var database = await Database().WaitAsync(cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        var target = LibraryPath.Canonical(folder);
        var isFolder = Directory.Exists(target);
        var root = isFolder ? target : Path.GetDirectoryName(target)!;
        var native = Environment.GetEnvironmentVariable("ECHO_METADATA_SCANNER");
        var rules = await database.GetFolderRulesAsync(cancellation).ConfigureAwait(false);
        var rule = rules.FirstOrDefault(item => LibraryPath.Key(item.Path) == LibraryPath.Key(Path.TrimEndingDirectorySeparator(root)));
        var ignore = new FolderIgnoreRules(root, rule?.Ignore ?? []);
        var recursive = options?.Recursive ?? rule?.Recursive ?? true;
        IFileDiscovery discovery = isFolder ? new LegacyFolderDiscovery(database, recursive, ignore.Matches) : new SelectedFileDiscovery(target, database);
        var scan = new ScanCoordinator(database, discovery, () => new LocalMetadataReader(native), admission);
        var limits = ScanLimits;
        return await scan.ScanAsync(new(root, limits.Workers, limits.Batch, Force: options?.Mode == FolderScanMode.EmbeddedTags,
            DetectMissing: isFolder && options?.Mode == FolderScanMode.Full && recursive && rule?.Ignore is not { Count: > 0 }), progress, cancellation).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _closed = true;
            return new(_disposal ??= Task.Run(ShutdownAsync));
        }
    }

    private async Task ShutdownAsync()
    {
        Task<LibraryDatabase>? opening;
        Task<ScanReport>? scan;
        lock (_gate) { _closed = true; opening = _opening; scan = _scan; }
        await _shutdown.CancelAsync().ConfigureAwait(false);
        LibraryDatabase? database = null;
        if (opening is not null)
        {
            try { database = await opening.ConfigureAwait(false); }
            catch (Exception) { /* Initialization errors are reported by the page. */ }
        }
        try
        {
            if (_folderWatch is not null) await _folderWatch.DisposeAsync().ConfigureAwait(false);
            if (scan is not null) { try { await scan.ConfigureAwait(false); } catch (Exception) { } }
        }
        finally
        {
            try { if (database is not null) await database.DisposeAsync().ConfigureAwait(false); }
            finally { FolderChanged = null; _shutdown.Dispose(); }
        }
    }
}
