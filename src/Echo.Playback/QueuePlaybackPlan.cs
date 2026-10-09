namespace Echo.Playback;

// User order stays editable. Shuffle is a stable permutation: pinned songs, then songs not just heard, then recent ones.
internal static class QueuePlaybackPlan
{
    public const int RecentAvoidance = 8;
    public const int PreviewLength = 6;
    private static readonly HashSet<string> NoRecent = new(StringComparer.Ordinal);
    private static readonly QueuePlanStep[] NoSteps = [];

    public static IReadOnlySet<string> RecentTrackIds(IReadOnlyList<QueueTrack> history, int limit = RecentAvoidance)
    {
        if (history.Count == 0 || limit <= 0) return NoRecent;
        var ids = new HashSet<string>(Math.Min(limit, history.Count), StringComparer.Ordinal);
        for (var index = history.Count - 1; index >= 0 && ids.Count < limit; index--)
            ids.Add(history[index].TrackId);
        return ids;
    }

    public static string[] Order(IReadOnlyList<QueueTrack> items, string? currentId, IReadOnlySet<string> recentTrackIds)
    {
        var currentIsBase = false;
        var fresh = new List<string>();
        var recent = new List<string>();
        foreach (var item in items)
        {
            if (item.QueuedNext) continue;
            if (item.ItemId == currentId) { currentIsBase = true; continue; }
            (recentTrackIds.Contains(item.TrackId) ? recent : fresh).Add(item.ItemId);
        }
        var freshIds = fresh.ToArray();
        var recentIds = recent.ToArray();
        Random.Shared.Shuffle(freshIds);
        Random.Shared.Shuffle(recentIds);
        if (!currentIsBase) return Join(freshIds, recentIds);
        var result = new string[freshIds.Length + recentIds.Length + 1];
        result[0] = currentId!;
        freshIds.CopyTo(result, 1);
        recentIds.CopyTo(result, 1 + freshIds.Length);
        return result;
    }

    public static void PlaceAdditions(List<string> order, IReadOnlyList<QueueTrack> items, IReadOnlyList<string> additionIds,
        string? currentId, IReadOnlySet<string> recentTrackIds)
    {
        if (additionIds.Count == 0) return;
        var tracks = new Dictionary<string, QueueTrack>(items.Count, StringComparer.Ordinal);
        foreach (var item in items) tracks[item.ItemId] = item;
        var fresh = new List<string>();
        var recent = new List<string>();
        foreach (var id in additionIds)
        {
            if (!tracks.TryGetValue(id, out var track)) continue;
            (recentTrackIds.Contains(track.TrackId) ? recent : fresh).Add(id);
        }
        var freshIds = fresh.ToArray();
        var recentIds = recent.ToArray();
        Random.Shared.Shuffle(freshIds);
        Random.Shared.Shuffle(recentIds);
        var insertAt = order.Count > 0 && order[0] == currentId ? 1 : 0;
        while (insertAt < order.Count && tracks.TryGetValue(order[insertAt], out var existing) && !recentTrackIds.Contains(existing.TrackId))
            insertAt++;
        order.InsertRange(insertAt, freshIds);
        order.AddRange(recentIds);
    }

    public static QueuePlanStep[] Upcoming(IReadOnlyList<QueueTrack> projected, string? currentId, bool repeatAll, bool shuffle,
        IReadOnlySet<string> recentTrackIds, int limit = PreviewLength)
    {
        if (projected.Count == 0 || limit <= 0) return NoSteps;
        var index = -1;
        for (var i = 0; i < projected.Count; i++)
            if (projected[i].ItemId == currentId) { index = i; break; }
        var start = index + 1;
        var available = repeatAll ? projected.Count : projected.Count - start;
        if (available <= 0) return NoSteps;
        var count = Math.Min(limit, available);
        var steps = new QueuePlanStep[count];
        var written = 0;
        for (var n = 0; n < count; n++)
        {
            var at = (start + n) % projected.Count;
            var item = projected[at];
            if (n > 0 && item.ItemId == currentId) break;
            var wrapped = index >= 0 && at < start;
            steps[written++] = new(item.ItemId, Reason(item, wrapped, shuffle, recentTrackIds));
        }
        return written == steps.Length ? steps : steps[..written];
    }

    private static QueuePlanReason Reason(QueueTrack item, bool wrapped, bool shuffle, IReadOnlySet<string> recentTrackIds)
    {
        if (item.QueuedNext) return QueuePlanReason.PinnedNext;
        if (wrapped) return QueuePlanReason.RepeatWrap;
        if (!shuffle) return QueuePlanReason.Sequential;
        return recentTrackIds.Contains(item.TrackId) ? QueuePlanReason.ShuffleRecent : QueuePlanReason.ShuffleFresh;
    }

    private static string[] Join(string[] left, string[] right)
    {
        if (right.Length == 0) return left;
        if (left.Length == 0) return right;
        var result = new string[left.Length + right.Length];
        left.CopyTo(result, 0);
        right.CopyTo(result, left.Length);
        return result;
    }
}
