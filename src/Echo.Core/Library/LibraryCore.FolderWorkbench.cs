using Echo.Contracts.Library;

namespace Echo.Core.Library;

public sealed partial class LibraryCore : IFolderWorkbench
{
    public async Task<FolderUiState> GetFolderUiStateAsync(CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetFolderUiStateAsync(cancellation).ConfigureAwait(false);
    public async Task SaveFolderUiStateAsync(FolderUiState state, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).SaveFolderUiStateAsync(state, cancellation).ConfigureAwait(false);
    public async Task<IReadOnlyList<FolderRule>> GetFolderRulesAsync(CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetFolderRulesAsync(cancellation).ConfigureAwait(false);
    public async Task SaveFolderRuleAsync(FolderRule rule, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).SaveFolderRuleAsync(rule, cancellation).ConfigureAwait(false);
    public async Task<FolderPage> SearchFoldersAsync(string search, int offset = 0, int limit = 100, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).SearchFoldersAsync(search, offset, limit, cancellation).ConfigureAwait(false);
    public async Task<FolderInsights> GetFolderInsightsAsync(FolderTrackQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetFolderInsightsAsync(query, cancellation).ConfigureAwait(false);
    public async Task<IReadOnlyList<FolderScanRecord>> GetFolderScanHistoryAsync(string? path, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetFolderScanHistoryAsync(path, cancellation).ConfigureAwait(false);
    public async Task StoreFolderResourcesAsync(IReadOnlyList<FolderResourceFact> facts, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).StoreFolderResourcesAsync(facts, cancellation).ConfigureAwait(false);
    public async Task RemoveFolderRootAsync(string path, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).RemoveFolderRootAsync(path, cancellation).ConfigureAwait(false);
    public async Task SetTracksHiddenAsync(IReadOnlyList<string> ids, bool hidden, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).SetTracksHiddenAsync(ids, hidden, cancellation).ConfigureAwait(false);
    public async Task<TrackTagState> GetTrackTagsAsync(string id, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetTrackTagsAsync(id, cancellation).ConfigureAwait(false);
    public async Task SaveTrackTagsAsync(string id, TrackTagEdit tags, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).SaveTrackTagsAsync(id, tags, cancellation).ConfigureAwait(false);
    public async Task RepairFolderIndexesAsync(CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).RepairFolderIndexesAsync(cancellation).ConfigureAwait(false);
}
