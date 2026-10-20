using Echo.Contracts.Library;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

// Server art is one request. A folder image is fetched once and reused for every song in that directory.
public sealed class RemoteSidecarArtwork(IAlbumArtwork local, Func<string, RemoteConnection?> source, IRemoteFileClient files) : IAlbumArtwork, IArtistArtwork
{
    public Task<byte[]?> ReadArtistAsync(string samplePath, CancellationToken cancellation)
    {
        if (RemoteTrackPaths.TrySplit(samplePath, out var sourceId, out var remotePath))
        {
            var connection = source(sourceId); var path = connection is null ? null : files.LocalPath(connection, remotePath);
            return path is not null && local is IArtistArtwork artist ? artist.ReadArtistAsync(path, cancellation) : Task.FromResult<byte[]?>(null);
        }
        return local is IArtistArtwork artwork ? artwork.ReadArtistAsync(samplePath, cancellation) : Task.FromResult<byte[]?>(null);
    }
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] Shared = ["cover", "folder", "front", "album", "song"];
    private readonly Dictionary<string, byte[]?> folders = new(StringComparer.Ordinal);
    private readonly object gate = new();
    public async Task<byte[]?> ReadAsync(string samplePath, CancellationToken cancellation)
    {
        if (!RemoteTrackPaths.TrySplit(samplePath, out var sourceId, out var remotePath)) return await local.ReadAsync(samplePath, cancellation).ConfigureAwait(false);
        var connection = source(sourceId);
        if (connection is null) return null;
        var localPath = files.LocalPath(connection, remotePath);
        if (localPath is not null) return await local.ReadAsync(localPath, cancellation).ConfigureAwait(false);
        var remote = await files.ReadArtworkAsync(connection, remotePath, cancellation).ConfigureAwait(false);
        if (remote is { Length: > 32 }) return remote;
        if (connection.Provider != "webdav") return null;
        var slash = remotePath.LastIndexOf('/');
        var directory = slash <= 0 ? "/" : remotePath[..(slash + 1)];
        var stem = Path.GetFileNameWithoutExtension(remotePath);
        foreach (var extension in Extensions)
        {
            var bytes = await ReadAsync(connection, directory + stem + extension, cancellation).ConfigureAwait(false);
            if (bytes is { Length: > 32 }) return bytes;
        }
        return await FolderAsync(connection, directory, cancellation).ConfigureAwait(false);
    }
    private async Task<byte[]?> FolderAsync(RemoteConnection connection, string directory, CancellationToken cancellation)
    {
        var key = connection.Id + "\n" + directory;
        lock (gate) { if (folders.TryGetValue(key, out var cached)) return cached; }
        byte[]? found = null;
        foreach (var name in Shared)
        {
            foreach (var extension in Extensions)
            {
                found = await ReadAsync(connection, directory + name + extension, cancellation).ConfigureAwait(false);
                if (found is { Length: > 32 }) break;
            }
            if (found is { Length: > 32 }) break;
        }
        lock (gate)
        {
            if (folders.Count >= 64) folders.Clear();
            folders[key] = found;
        }
        return found;
    }
    private async Task<byte[]?> ReadAsync(RemoteConnection connection, string path, CancellationToken cancellation)
    {
        try { return await files.ReadFileAsync(connection, path, 4 * 1024 * 1024, cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }
}
