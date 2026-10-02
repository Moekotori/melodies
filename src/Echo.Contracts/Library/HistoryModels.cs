namespace Echo.Contracts.Library;

public sealed record HistoryFilter(string Search = "", long? FromUnix = null, long? ToUnix = null, bool CountedOnly = false);
public sealed record HistoryCursor(long StartedUnix, string Id, long Snapshot, long Epoch, string QueryKey, bool Newer = false);
public sealed record HistoryRecord(string Id, TrackRow Track, long StartedUnix, long UpdatedUnix,
    long? EndedUnix, double PlayedSeconds, bool Counted, string Source, bool Available);
public sealed record HistoryDay(string Date, long Plays, long Tracks, double Seconds, long Pending);
public sealed record HistoryPage(IReadOnlyList<HistoryRecord> Items, HistoryCursor? Next, long Snapshot, long Epoch,
    IReadOnlyList<HistoryDay> Days, long? Total, HistoryCursor? Previous = null);
public sealed record HistoryTotals(long Plays, long Counted, double Seconds, long Tracks, long Artists, long ActiveDays);
public sealed record HistoryRank(string Name, string Subtitle, long Plays, double Seconds);
public sealed record HistorySessions(long Count, double AverageSeconds, double LongestSeconds, long LongestStreak);
public sealed record HistoryDiscovery(long NewTracks, long ReturningTracks, long ReplayedTracks, long RediscoveredTracks, double RepeatRate);
public sealed record HistoryReview(HistoryTotals Totals, HistoryTotals? Previous, IReadOnlyList<ListeningDay> Days,
    IReadOnlyList<HistoryRank> Tracks, IReadOnlyList<HistoryRank> Artists, IReadOnlyList<HistoryRank> Albums,
    IReadOnlyList<HistoryRank> Formats, HistorySessions Sessions, HistoryDiscovery Discovery, IReadOnlyList<HistoryRank> Quality);

public interface IHistoryLibrary
{
    Task<long> GetHistoryEpochAsync(CancellationToken cancellation = default);
    Task<HistoryPage> GetHistoryAsync(HistoryFilter filter, HistoryCursor? after = null, int limit = 200, CancellationToken cancellation = default);
    Task<HistoryReview> GetHistoryReviewAsync(HistoryFilter filter, CancellationToken cancellation = default);
    Task<long> ClearHistoryAsync(CancellationToken cancellation = default);
    Task<long> PruneInvalidHistoryAsync(CancellationToken cancellation = default);
}
