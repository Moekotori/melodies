namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private int shuffleAvoidRecentCount;
    private readonly Queue<string> shuffleHistory = new();
    private int shuffleHistoryCharacters;

    public Task SetShuffleAvoidRecentCountAsync(int count, CancellationToken token = default)
    {
        if (count is not (0 or 25 or 100)) throw new ArgumentOutOfRangeException(nameof(count));
        return Command(async () =>
        {
            if (shuffleAvoidRecentCount == count) return;
            var priorCount = shuffleAvoidRecentCount;
            var priorOrder = shuffleOrder;
            shuffleAvoidRecentCount = count;
            if (shuffle) shuffleOrder = CreateShuffleOrder(queue, snapshot.Current?.ItemId);
            try { if (initialized && shuffle) await SendQueueAsync(queue, snapshot.Current?.ItemId, repeat).ConfigureAwait(false); }
            catch { shuffleAvoidRecentCount = priorCount; shuffleOrder = priorOrder; throw; }
            PublishQueue();
        }, token, affectsPlayback: false);
    }

    private void RememberShuffleTrack(string trackId)
    {
        // Keep identities independently of the metadata/navigation history and removed occurrences.
        if (trackId.Length > 32768) return;
        shuffleHistory.Enqueue(trackId);
        shuffleHistoryCharacters += trackId.Length;
        while (shuffleHistory.Count > 100 || shuffleHistoryCharacters > 32768)
            shuffleHistoryCharacters -= shuffleHistory.Dequeue().Length;
    }

    private IReadOnlySet<string> RecentShuffleTracks()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in shuffleHistory.TakeLast(shuffleAvoidRecentCount)) ids.Add(id);
        // A duplicate occurrence of the active song always goes behind other songs.
        if (snapshot.Current is { } current) ids.Add(current.TrackId);
        return ids;
    }

    private bool RefreshShuffleOrder()
    {
        if (!shuffle) return false;
        var updated = QueuePlaybackPlan.ReorderRemaining(shuffleOrder, queue, snapshot.Current?.ItemId, RecentShuffleTracks());
        if (ReferenceEquals(updated, shuffleOrder)) return false;
        shuffleOrder = updated;
        return true;
    }
}
