namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    public Task SelectQueueItemAsync(string itemId, CancellationToken token = default) => Command(async () =>
    {
        var track = queue.FirstOrDefault(x => x.ItemId == itemId) ?? throw new ArgumentException("队列项已移除");
        await SwitchToAsync(track).ConfigureAwait(false);
    }, token);

    public Task NextAsync(CancellationToken token = default) => Command(async () =>
    {
        var track = ResolveNext();
        if (track is null) { await StopNowAsync().ConfigureAwait(false); return; }
        await SwitchToAsync(track).ConfigureAwait(false);
    }, token);

    public Task PreviousAsync(CancellationToken token = default) => Command(async () =>
    {
        QueueTrack? previous = null;
        var current = snapshot.Current;
        var historyIndex = history.Length - 1;
        if (!shuffle && current is { QueuedNext: false, Origin.Kind: "album" })
        {
            var index = Array.FindIndex(queue, x => x.ItemId == current.ItemId);
            previous = queue.Take(Math.Max(0, index)).LastOrDefault(x => !x.QueuedNext && x.Origin?.Id == current.Origin.Id);
            historyIndex = previous is null ? -1 : Array.FindLastIndex(history, x => x.ItemId == previous.ItemId);
        }
        else previous = history.LastOrDefault();
        if (previous is null) return;
        var trimmed = history.Take(Math.Max(0, historyIndex)).ToArray();
        var items = queue;
        if (!items.Any(x => x.ItemId == previous.ItemId))
        {
            if (items.Length >= QueueCapacity) throw new InvalidOperationException("队列已满，请先移除曲目");
            var index = Array.FindIndex(items, x => x.ItemId == current?.ItemId);
            var list = items.ToList(); list.Insert(Math.Max(0, index), previous); items = list.ToArray();
        }
        await EnsureStartedAsync().ConfigureAwait(false);
        await SendQueueAsync(items, previous.ItemId, repeat).ConfigureAwait(false);
        await OpenAsync(previous).ConfigureAwait(false);
        SetQueueItems(RemoveCompleted(items, current, previous)); history = trimmed;
        await SendQueueAsync(queue, previous.ItemId, repeat).ConfigureAwait(false);
        PublishQueue();
    }, token);

    private async Task SwitchToAsync(QueueTrack track)
    {
        await EnsureStartedAsync().ConfigureAwait(false);
        var previous = snapshot.Current;
        var previousQueue = queue;
        await SendQueueAsync(queue, track.ItemId, repeat).ConfigureAwait(false);
        await OpenAsync(track).ConfigureAwait(false);
        CompleteTransition(previous, track);
        if (!ReferenceEquals(previousQueue, queue)) await SendQueueAsync(queue, track.ItemId, repeat).ConfigureAwait(false);
        PublishQueue();
    }

    private static QueueTrack[] RemoveCompleted(QueueTrack[] items, QueueTrack? previous, QueueTrack next)
        => previous is not null && previous.ItemId != next.ItemId && (previous.QueuedNext || previous.RemoveAfterPlay)
            ? items.Where(x => x.ItemId != previous.ItemId).ToArray() : items;

    private void CompleteTransition(QueueTrack? previous, QueueTrack next)
    {
        if (previous is not null && previous.ItemId != next.ItemId)
        {
            if (!previous.RemoveAfterPlay && queue.Any(x => x.ItemId == previous.ItemId)) history = history.Append(previous).TakeLast(500).ToArray();
            // Keep only a bounded metadata history, even for long path/title strings.
            while (history.Length > 0 && history.Sum(x => x.FilePath.Length + x.ItemId.Length + x.TrackId.Length + (x.Source?.Uri.Length ?? 0)
                + (x.Metadata?.Sum(p => p.Key.Length + (p.Value?.Length ?? 0)) ?? 0)
                + (x.Source?.Headers?.Sum(p => p.Key.Length + p.Value.Length) ?? 0)) > 32768)
                history = history[1..];
            SetQueueItems(RemoveCompleted(queue, previous, next));
            undo = null;
        }
    }

    public Task SetQueueModeAsync(QueuePlaybackMode mode, CancellationToken token = default) => Command(async () =>
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        await EnsureStartedAsync().ConfigureAwait(false);
        var oldShuffle = shuffle; var oldOrder = shuffleOrder;
        shuffle = mode == QueuePlaybackMode.Shuffle;
        if (shuffle && !oldShuffle) shuffleOrder = CreateShuffleOrder(queue, snapshot.Current?.ItemId);
        var nextRepeat = mode == QueuePlaybackMode.RepeatOne ? RepeatMode.One : mode == QueuePlaybackMode.RepeatAll ? RepeatMode.All : RepeatMode.Off;
        try { await SendQueueAsync(queue, snapshot.Current?.ItemId, nextRepeat).ConfigureAwait(false); }
        catch { shuffle = oldShuffle; shuffleOrder = oldOrder; throw; }
        repeat = nextRepeat;
        PublishQueue(); Publish(snapshot);
    }, token, affectsPlayback: false);

    public Task SetRepeatAsync(RepeatMode mode, CancellationToken token = default)
        => SetQueueModeAsync(mode == RepeatMode.All ? QueuePlaybackMode.RepeatAll : mode == RepeatMode.One ? QueuePlaybackMode.RepeatOne : QueuePlaybackMode.Sequential, token);
}
