namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private sealed record QueueEdit(QueueTrack[] Items, QueueTrack[] History, string[] ShuffleOrder);
    private QueueEdit? undo;
    public bool CanUndoQueueEdit => undo is not null;

    public Task EnqueueAsync(IEnumerable<QueueTrack> tracks, bool nextUp = false, CancellationToken token = default)
    {
        var additions = ReadItems(tracks).Select(x => x with { QueuedNext = nextUp }).ToArray();
        if (additions.Length == 0) return Task.CompletedTask;
        return EditQueueAsync(items =>
        {
            foreach (var track in additions) (track.Source ?? AudioSource.Local(track.FilePath)).Validate();
            var index = items.Length;
            if (nextUp)
            {
                index = Array.FindIndex(items, x => x.ItemId == snapshot.Current?.ItemId) + 1;
                while (index < items.Length && items[index].QueuedNext) ++index;
            }
            return [.. items.Take(index), .. additions, .. items.Skip(index)];
        }, token);
    }

    public Task RemoveQueueItemsAsync(IEnumerable<string> ids, CancellationToken token = default)
    {
        var selected = ids.Take(QueueCapacity).ToHashSet(StringComparer.Ordinal);
        return EditQueueAsync(items => items.Where(x => !selected.Contains(x.ItemId)).ToArray(), token,
            retainedHistory => retainedHistory.Where(x => !selected.Contains(x.ItemId)).ToArray());
    }
    public Task ClearQueueAsync(CancellationToken token = default)
        => EditQueueAsync(_ => [], token, _ => []);

    public Task MoveQueueItemsAsync(IEnumerable<string> ids, int targetIndex, CancellationToken token = default)
    {
        var selected = ids.Take(QueueCapacity).ToHashSet(StringComparer.Ordinal);
        return EditQueueAsync(items =>
        {
            var moving = items.Where(x => selected.Contains(x.ItemId)).ToArray();
            var remaining = items.Where(x => !selected.Contains(x.ItemId)).ToArray();
            var index = Math.Clamp(targetIndex - items.Take(Math.Clamp(targetIndex, 0, items.Length)).Count(x => selected.Contains(x.ItemId)), 0, remaining.Length);
            return [.. remaining.Take(index), .. moving, .. remaining.Skip(index)];
        }, token);
    }

    public Task MoveQueueItemsNextAsync(IEnumerable<string> ids, CancellationToken token = default)
    {
        var selected = ids.Take(QueueCapacity).ToHashSet(StringComparer.Ordinal);
        return EditQueueAsync(items =>
        {
            selected.Remove(snapshot.Current?.ItemId ?? "");
            var moving = items.Where(x => selected.Contains(x.ItemId)).Select(x => x with { QueuedNext = true }).ToArray();
            var remaining = items.Where(x => !selected.Contains(x.ItemId)).ToArray();
            var index = Array.FindIndex(remaining, x => x.ItemId == snapshot.Current?.ItemId) + 1;
            while (index < remaining.Length && remaining[index].QueuedNext) ++index;
            return [.. remaining.Take(index), .. moving, .. remaining.Skip(index)];
        }, token);
    }

    public Task SetRemoveAfterPlayAsync(IEnumerable<string> ids, bool enabled, CancellationToken token = default)
    {
        var selected = ids.Take(QueueCapacity).ToHashSet(StringComparer.Ordinal);
        return EditQueueAsync(items => items.Select(x => selected.Contains(x.ItemId) ? x with { RemoveAfterPlay = enabled } : x).ToArray(), token);
    }

    private Task EditQueueAsync(Func<QueueTrack[], QueueTrack[]> edit, CancellationToken token,
        Func<QueueTrack[], QueueTrack[]>? editHistory = null) => Command(async () =>
    {
        var items = edit(queue); ValidateItems(items);
        await EnsureStartedAsync().ConfigureAwait(false);
        var previous = new QueueEdit(queue, history, shuffleOrder);
        var removedCurrent = snapshot.Current is not null && !items.Any(x => x.ItemId == snapshot.Current.ItemId);
        var order = shuffleOrder.Where(id => items.Any(x => x.ItemId == id)).ToList();
        var additions = items.Where(x => !x.QueuedNext && !order.Contains(x.ItemId)).Select(x => x.ItemId).ToArray();
        Random.Shared.Shuffle(additions); order.AddRange(additions); shuffleOrder = order.ToArray();
        try { await SendQueueAsync(items, snapshot.Current?.ItemId, removedCurrent ? RepeatMode.Off : repeat).ConfigureAwait(false); }
        catch { shuffleOrder = previous.ShuffleOrder; throw; }
        SetQueueItems(items); history = editHistory?.Invoke(history) ?? history; undo = previous;
        Publish(snapshot with { Current = items.FirstOrDefault(x => x.ItemId == snapshot.Current?.ItemId) ?? snapshot.Current });
        PublishQueue();
    }, token, affectsPlayback: false);

    public Task UndoQueueEditAsync(CancellationToken token = default) => Command(async () =>
    {
        if (undo is not { } previous) return;
        var oldOrder = shuffleOrder; shuffleOrder = previous.ShuffleOrder;
        try { await SendQueueAsync(previous.Items, snapshot.Current?.ItemId, repeat).ConfigureAwait(false); }
        catch { shuffleOrder = oldOrder; throw; }
        SetQueueItems(previous.Items); history = previous.History; undo = null;
        Publish(snapshot with { Current = queue.FirstOrDefault(x => x.ItemId == snapshot.Current?.ItemId) ?? snapshot.Current });
        PublishQueue();
    }, token, affectsPlayback: false);
}
