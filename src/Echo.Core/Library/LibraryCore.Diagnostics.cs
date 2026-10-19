using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Core.Library;
public sealed partial class LibraryCore : IAlbumMetadata
{
    public async Task<LibraryStats> GetStatsAsync(CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetStatsAsync(cancellation).ConfigureAwait(false);
    public async Task<IReadOnlyList<ScanIssue>> GetScanIssuesAsync(string jobId, int limit = 16, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetScanIssuesAsync(jobId, limit, cancellation).ConfigureAwait(false);
    public async Task<AlbumMetadataSnapshot> GetAlbumMetadataAsync(string albumId, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetAlbumMetadataAsync(albumId, cancellation).ConfigureAwait(false);
    public async Task SetAlbumMetadataAsync(string albumId, AlbumMetadataField field, MetadataSource source, string? value, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).SetAlbumMetadataAsync(albumId, field, source, value, cancellation).ConfigureAwait(false);
}
