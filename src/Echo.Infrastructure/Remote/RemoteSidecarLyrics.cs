using Echo.Infrastructure.Lyrics;
using Echo.Contracts.Lyrics;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

public sealed class RemoteSidecarLyrics(ILyricsSource local, Func<string, RemoteConnection?> source, IRemoteFileClient files) : ILyricsSource, IAsyncDisposable
{
    private static IReadOnlyList<string> Extensions => LyricsFileFormats.Extensions;
    public async Task<LyricsText?> ReadAsync(string audioPath, string? explicitPath, CancellationToken cancellation)
    {
        if (!RemoteTrackPaths.TrySplit(audioPath, out var sourceId, out var remotePath))
            return await local.ReadAsync(audioPath, explicitPath, cancellation).ConfigureAwait(false);
        if (explicitPath is not null) return await local.ReadAsync(audioPath, explicitPath, cancellation).ConfigureAwait(false);
        var connection = source(sourceId);
        if (connection is null) return null;
        if (connection.Provider is "subsonic" or "jellyfin" or "emby")
        {
            string? server;
            try { server = await files.ReadServerLyricsAsync(connection, remotePath, cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { server = null; }
            if (string.IsNullOrWhiteSpace(server) || server.Length > 512 * 1024) return null;
            var kind = server.Contains('[') ? ".lrc" : ".txt";
            return new(server, kind, "server" + kind);
        }
        if (connection.Provider is not ("webdav" or "smb" or "sshfs")) return null;
        var slash = remotePath.LastIndexOf('/');
        var directory = slash <= 0 ? "/" : remotePath[..(slash + 1)];
        var stem = Path.GetFileNameWithoutExtension(remotePath);
        foreach (var name in Names(directory, stem))
        {
            cancellation.ThrowIfCancellationRequested();
            byte[]? bytes = null;
            try { bytes = await files.ReadFileAsync(connection, name, 1024 * 1024, cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { }
            if (bytes is not { Length: > 0 }) continue;
            try
            {
                var text = await LocalLyricsReader.DecodeAsync(bytes, name, cancellation).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text.Text)) return text;
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or IOException) { }
        }
        return null;
    }

    private static IEnumerable<string> Names(string directory, string stem)
    {
        foreach (var extension in Extensions) yield return directory + stem + extension;
        foreach (var extension in Extensions) yield return directory + "lyrics/" + stem + extension;
    }
    public async ValueTask DisposeAsync()
    {
        if (local is IAsyncDisposable disposable) await disposable.DisposeAsync().ConfigureAwait(false);
    }
}
