namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private (QueueTrack[] Items, RepeatMode Mode) ProjectPlayableQueue(QueueTrack[] projected, string? current, RepeatMode mode)
    {
        if (!projected.Any(IsUnresolvedRemote)) return (projected, mode);
        var index = Array.FindIndex(projected, item => item.ItemId == current);
        if (index < 0) return ([], RepeatMode.Off);
        var items = new List<QueueTrack>(Math.Min(4, projected.Length));
        var count = mode == RepeatMode.One ? 1 : mode == RepeatMode.All ? projected.Length : projected.Length - index;
        for (var offset = 0; offset < count; offset++)
        {
            var item = projected[(index + offset) % projected.Length];
            if (IsUnresolvedRemote(item))
            {
                // Resolve only when opening a track. The native queue stops at an unresolved
                // boundary; Core handles its EOF once and opens the next business identity.
                if (offset != 0 || snapshot.Current?.ItemId != item.ItemId || snapshot.OperationId <= 0
                    || currentPlayableSource is null) break;
                item = item with { Source = currentPlayableSource };
            }
            items.Add(item);
        }
        var repeat = mode == RepeatMode.One && items.Count == 1 || items.Count == projected.Length ? mode : RepeatMode.Off;
        return (items.ToArray(), repeat);
    }

    private static bool IsUnresolvedRemote(QueueTrack item) => item.Source?.Kind == "remote"
        || item.Source is null && item.FilePath.StartsWith("remote://", StringComparison.Ordinal);
}
