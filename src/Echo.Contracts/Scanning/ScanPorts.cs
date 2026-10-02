using Echo.Contracts.Library;

namespace Echo.Contracts.Scanning;

public interface IFileDiscovery
{
    IEnumerable<FileSnapshot> Enumerate(string root, bool requireFullCoverage, CancellationToken cancellation, bool force = false);
}
public interface IMetadataReader : IAsyncDisposable
{
    ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, CancellationToken cancellation);
}
public interface ILibraryStore : IAsyncDisposable
{
    Task<string> BeginScanAsync(string root, bool trackPresence, CancellationToken cancellation);
    Task<IReadOnlyDictionary<string, KnownTrack>> FindKnownAsync(
        IReadOnlyList<FileSnapshot> files, CancellationToken cancellation);
    Task<BatchReceipt> CommitScanBatchAsync(string jobId, long sequence, IReadOnlyList<ScanEntry> entries,
        CancellationToken cancellation);
    Task<long> FinishScanAsync(string jobId, ScanStatus status, long discovered,
        bool detectMissing, string? errorCode, CancellationToken cancellation);
    Task<TrackPage> GetTracksAsync(TrackQuery query, CancellationToken cancellation = default);
    Task<LibraryStats> GetStatsAsync(CancellationToken cancellation = default);
    Task<IReadOnlyList<ScanIssue>> GetScanIssuesAsync(string jobId, int limit = 16,
        CancellationToken cancellation = default);
    Task<IReadOnlyDictionary<string, SourceCacheItem>> ReadSourceCacheAsync(
        IReadOnlyList<FileSnapshot> files, CancellationToken cancellation);
    Task StoreSourceCacheAsync(IReadOnlyList<SourceCacheItem> items, CancellationToken cancellation);
}
