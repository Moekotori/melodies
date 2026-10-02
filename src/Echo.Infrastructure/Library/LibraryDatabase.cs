using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : ILibraryStore, ITrackLibrary, Echo.Contracts.Library.IAlbumLibrary, Echo.Contracts.Library.IAlbumPersonalState, Echo.Contracts.Library.IArtistLibrary
{
    private readonly FileStream _ownership;
    private readonly SqliteLane _writer;
    private readonly SqliteLane _interactiveReader;
    private readonly SqliteLane _scanReader;
    private int _disposed;

    private LibraryDatabase(FileStream ownership, SqliteLane writer, SqliteLane interactive, SqliteLane scan)
    { _ownership = ownership; _writer = writer; _interactiveReader = interactive; _scanReader = scan; }

    public static async Task<LibraryDatabase> OpenAsync(string databasePath, CancellationToken cancellation = default)
    {
        var path = LibraryPath.Canonical(databasePath);
        var ownership = await Task.Run(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new FileStream(path + ".writer.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }, CancellationToken.None).ConfigureAwait(false);
        SqliteLane? writer = null, interactive = null, scan = null;
        try
        {
            writer = new(path, false, SchemaMigrator.Initialize);
            await writer.Ready.WaitAsync(cancellation).ConfigureAwait(false);
            interactive = new(path, true);
            scan = new(path, true);
            await Task.WhenAll(interactive.Ready, scan.Ready).WaitAsync(cancellation).ConfigureAwait(false);
            return new(ownership, writer, interactive, scan);
        }
        catch
        {
            foreach (var lane in new[] { scan, interactive, writer })
                if (lane is not null) { try { await lane.DisposeAsync().ConfigureAwait(false); } catch { } }
            ownership.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            // Drain admitted writes before releasing the ownership lock.
            await Task.WhenAll(_interactiveReader.DisposeAsync().AsTask(),
                _scanReader.DisposeAsync().AsTask(), _writer.DisposeAsync().AsTask()).ConfigureAwait(false);
        }
        finally { _ownership.Dispose(); }
    }
}
