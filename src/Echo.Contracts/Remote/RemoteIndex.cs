namespace Echo.Contracts.Remote;

public sealed record RemoteIndexTrack(string SourceId, string RemotePath, string Title, string Artist, string Album,
    long SizeBytes, long ModifiedTicks, double DurationSeconds, string Codec, string AlbumArtist = "", int? TrackNumber = null,
    int? DiscNumber = null, int? Year = null, string Genre = "", int SampleRate = 0, int BitDepth = 0);
public sealed record RemoteLibraryPage(IReadOnlyList<RemoteIndexTrack> Tracks, bool Partial);
public sealed record RemoteIndexReceipt(int Inserted, int Updated, int Unchanged);
public sealed record RemoteSyncProgress(int Directories, int Discovered, int Inserted = 0, int Updated = 0, int Unchanged = 0);
public sealed record RemoteSyncResult(int Inserted, int Updated, int Unchanged, int Missing);
public interface IRemoteTrackIndex
{
    Task<RemoteIndexReceipt> WriteAsync(IReadOnlyList<RemoteIndexTrack> tracks, string syncToken, CancellationToken token);
    Task<int> RetireUnseenAsync(string sourceId, string syncToken, CancellationToken token);
    Task<int> RetireSourceAsync(string sourceId, CancellationToken token);
    Task<IReadOnlyList<string>> PresentAsync(string sourceId, IReadOnlyList<string> remotePaths, CancellationToken token);
    Task<string?> ReadAlbumCacheAsync(string sourceId, string albumId, string fingerprint, CancellationToken token);
    Task WriteAlbumCacheAsync(string sourceId, string albumId, string fingerprint, string payload, CancellationToken token);
}
public interface IRemoteIndexSync
{
    Task<RemoteSyncResult> SyncSourceAsync(string sourceId, IProgress<RemoteSyncProgress> progress, CancellationToken token);
}
