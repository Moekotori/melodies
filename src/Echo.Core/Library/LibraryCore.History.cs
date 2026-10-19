using Echo.Contracts.Library;

namespace Echo.Core.Library;

public sealed partial class LibraryCore : IHistoryLibrary
{
    public async Task<long> GetHistoryEpochAsync(CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetHistoryEpochAsync(cancellation).ConfigureAwait(false);
    public async Task<HistoryPage> GetHistoryAsync(HistoryFilter filter, HistoryCursor? after = null, int limit = 200, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetHistoryAsync(filter, after, limit, cancellation).ConfigureAwait(false);
    public async Task<HistoryReview> GetHistoryReviewAsync(HistoryFilter filter, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetHistoryReviewAsync(filter, cancellation).ConfigureAwait(false);
    public async Task<long> ClearHistoryAsync(CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).ClearHistoryAsync(cancellation).ConfigureAwait(false);
    public async Task<long> PruneInvalidHistoryAsync(CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).PruneInvalidHistoryAsync(cancellation).ConfigureAwait(false);
}
