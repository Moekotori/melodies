using Echo.Contracts.Scanning;

namespace Echo.Contracts.Library;

public enum FolderInsight { All, Recent, Unplayed, Lossless, HiRes, MissingCover, MissingLyrics, Duplicates }
public sealed record FolderRule(string Path, bool Pinned = false, int Position = 0, bool IncludeInCombined = true,
    bool Recursive = true, FolderTrackSort Sort = FolderTrackSort.Path, bool Descending = false,
    bool Watch = false, bool HideDuplicates = false, IReadOnlyList<string>? Ignore = null, string Key = "");
public sealed record FolderInsights(long Recent, long Unplayed, long Lossless, long HiRes,
    long MissingCover, long MissingLyrics, long Duplicates, long UncheckedResources);
public sealed record FolderScanRecord(string Id, string Path, ScanStatus Status, long Discovered, long Processed,
    long Inserted, long Updated, long Unchanged, long Failed, long Missing, long Started, long? Finished);
public sealed record FolderResourceFact(string TrackId, bool Cover, bool Lyrics);
public sealed record TrackTagEdit(string Title, string Artist, string Album, string AlbumArtist,
    int? TrackNumber = null, int? DiscNumber = null, int? Year = null, string Genre = "");
public sealed record TrackTagState(TrackTagEdit Tags, string Path, string? AudioPath, bool Virtual);
public sealed record FolderUiState(string? LastPath = null, double Width = 208, bool TreeCompact = false, bool TracksCompact = false,
    IReadOnlyList<string>? Expanded = null, IReadOnlyList<string>? SelectedRoots = null);

// Library facts and mutations share the application's existing database owner.
public interface IFolderWorkbench : IFolderLibrary
{
    Task<IReadOnlyList<FolderRule>> GetFolderRulesAsync(CancellationToken cancellation = default);
    Task SaveFolderRuleAsync(FolderRule rule, CancellationToken cancellation = default);
    Task<FolderPage> SearchFoldersAsync(string search, int offset = 0, int limit = 100, CancellationToken cancellation = default);
    Task<FolderInsights> GetFolderInsightsAsync(FolderTrackQuery query, CancellationToken cancellation = default);
    Task<IReadOnlyList<FolderScanRecord>> GetFolderScanHistoryAsync(string? path, CancellationToken cancellation = default);
    Task StoreFolderResourcesAsync(IReadOnlyList<FolderResourceFact> facts, CancellationToken cancellation = default);
    Task RemoveFolderRootAsync(string path, CancellationToken cancellation = default);
    Task SetTracksHiddenAsync(IReadOnlyList<string> ids, bool hidden, CancellationToken cancellation = default);
    Task<TrackTagState> GetTrackTagsAsync(string id, CancellationToken cancellation = default);
    Task SaveTrackTagsAsync(string id, TrackTagEdit tags, CancellationToken cancellation = default);
    Task RepairFolderIndexesAsync(CancellationToken cancellation = default);
    Task<FolderUiState> GetFolderUiStateAsync(CancellationToken cancellation = default);
    Task SaveFolderUiStateAsync(FolderUiState state, CancellationToken cancellation = default);
}
