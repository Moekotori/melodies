using Echo.Contracts.Remote;

namespace Echo.Core.Library;

public sealed partial class LibraryCore : IRemoteTrackIndex
{
    public async Task<RemoteIndexReceipt> WriteAsync(IReadOnlyList<RemoteIndexTrack> tracks, string syncToken, CancellationToken token)
        => await (await Database().WaitAsync(token).ConfigureAwait(false)).WriteAsync(tracks, syncToken, token).ConfigureAwait(false);
    public async Task<int> RetireUnseenAsync(string sourceId, string syncToken, CancellationToken token)
        => await (await Database().WaitAsync(token).ConfigureAwait(false)).RetireUnseenAsync(sourceId, syncToken, token).ConfigureAwait(false);
    public async Task<int> RetireSourceAsync(string sourceId, CancellationToken token)
        => await (await Database().WaitAsync(token).ConfigureAwait(false)).RetireSourceAsync(sourceId, token).ConfigureAwait(false);
    public async Task<IReadOnlyList<string>> PresentAsync(string sourceId, IReadOnlyList<string> remotePaths, CancellationToken token)
        => await (await Database().WaitAsync(token).ConfigureAwait(false)).PresentAsync(sourceId, remotePaths, token).ConfigureAwait(false);
    public async Task<string?> ReadAlbumCacheAsync(string sourceId, string albumId, string fingerprint, CancellationToken token)
        => await (await Database().WaitAsync(token).ConfigureAwait(false)).ReadAlbumCacheAsync(sourceId, albumId, fingerprint, token).ConfigureAwait(false);
    public async Task WriteAlbumCacheAsync(string sourceId, string albumId, string fingerprint, string payload, CancellationToken token)
        => await (await Database().WaitAsync(token).ConfigureAwait(false)).WriteAlbumCacheAsync(sourceId, albumId, fingerprint, payload, token).ConfigureAwait(false);
}
