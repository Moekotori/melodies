namespace Echo.Contracts.Library;

public sealed record FolderRow(string Path, string Name, long TrackCount, double DurationSeconds,
    long SizeBytes, string? SamplePath = null);
public sealed record FolderQuery(string? ParentPath = null, int Offset = 0, int Limit = 100);
public sealed record FolderPage(IReadOnlyList<FolderRow> Items, bool HasMore);
public enum FolderTrackSort { Path, Title, Artist, Album, Duration }
public sealed record FolderTrackQuery(string Path, int Offset = 0, int Limit = 200, string? Search = null,
    bool Recursive = true, bool LosslessOnly = false, FolderTrackSort Sort = FolderTrackSort.Path,
    bool Descending = false, long? Revision = null);
public sealed record FolderTrackPage(IReadOnlyList<TrackRow> Items, long Total, long Revision,
    double DurationSeconds, long SizeBytes, long LosslessCount);

public interface IFolderLibrary
{
    Task<FolderPage> GetFoldersAsync(FolderQuery query, CancellationToken cancellation = default);
    Task<FolderTrackPage> GetFolderTracksAsync(FolderTrackQuery query, CancellationToken cancellation = default);
}
