using System.Text.Json;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    public const int QueueCapacity = 1000;
    private QueueTrack[] history = [];
    private string[] shuffleOrder = [];
    private bool shuffle;
    private long queueVersion;
    private QueueSnapshot queueSnapshot = new(0, [], [], null, null, QueuePlaybackMode.Sequential);
    public QueueSnapshot QueueState => Volatile.Read(ref queueSnapshot);
    public QueuePlaybackMode QueueMode => repeat == RepeatMode.One ? QueuePlaybackMode.RepeatOne
        : shuffle ? QueuePlaybackMode.Shuffle : repeat == RepeatMode.All ? QueuePlaybackMode.RepeatAll : QueuePlaybackMode.Sequential;
    public event Action<QueueSnapshot>? QueueChanged;

    public Task PlayAsync(QueueTrack track, CancellationToken token = default) => SetQueueAsync([track], track.ItemId, token);

    public Task SetQueueAsync(IEnumerable<QueueTrack> tracks, string? startItemId = null, CancellationToken token = default)
    {
        var items = ReadItems(tracks);
        if (startItemId is not null && !items.Any(x => x.ItemId == startItemId)) throw new ArgumentException("队列中不存在起始项");
        return Command(async () =>
        {
            foreach (var item in items) AudioSource.Playable(item).Validate();
            await EnsureStartedAsync().ConfigureAwait(false);
            var current = startItemId ?? (items.Any(x => x.ItemId == snapshot.Current?.ItemId) ? snapshot.Current?.ItemId : null);
            var oldOrder = shuffleOrder;
            shuffleOrder = CreateShuffleOrder(items, current);
            try { await SendQueueAsync(items, current, repeat).ConfigureAwait(false); }
            catch { shuffleOrder = oldOrder; throw; }
            SetQueueItems(items); history = []; undo = null;
            if (startItemId is not null) await OpenAsync(items.First(x => x.ItemId == startItemId)).ConfigureAwait(false);
            else if (items.Length == 0 || current is null && snapshot.Current is not null) await StopNowAsync().ConfigureAwait(false);
            else Publish(snapshot);
            PublishQueue();
        }, token);
    }

    private static QueueTrack[] ReadItems(IEnumerable<QueueTrack> tracks)
    {
        var items = tracks.Take(QueueCapacity + 1).ToArray();
        ValidateItems(items);
        // Detach caller-owned dictionaries before admission to the single command owner.
        return items.Select(x => x with
        {
            Metadata = x.Metadata is null ? null : new System.Collections.ObjectModel.ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(x.Metadata)),
            Source = x.Source is null ? null : x.Source with { Headers = x.Source.Headers is null ? null
                : new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(x.Source.Headers)) }
        }).ToArray();
    }

    private static void ValidateItems(QueueTrack[] items)
    {
        if (items.Length > QueueCapacity) throw new ArgumentException("播放窗口最多 1000 项，请分页提供后续队列");
        if (items.Any(x => string.IsNullOrWhiteSpace(x.ItemId) || string.IsNullOrWhiteSpace(x.TrackId)
            || string.IsNullOrWhiteSpace(x.FilePath) || x.Metadata is { Count: > 24 } || x.SampleRate is <= 0 or > 24576000)
            || items.Select(x => x.ItemId).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("队列项必须有唯一 itemId 和有效音源");
        if (items.Sum(TrackTextLength) > 256 * 1024)
            throw new ArgumentException("队列内容超过内存预算，请减少曲目或附带信息");
        if (JsonSerializer.SerializeToUtf8Bytes(new NativeQueue(0, null, "off", items), PlaybackJsonContext.Default.NativeQueue).Length > 384 * 1024)
            throw new ArgumentException("队列内容超过内存预算，请减少曲目或附带信息");
    }

    private static long TrackTextLength(QueueTrack x) => (long)x.FilePath.Length + x.TrackId.Length + x.ItemId.Length
        + (x.Source?.Uri.Length ?? 0) + (x.Source?.Kind.Length ?? 0)
        + (x.Metadata?.Sum(p => (long)p.Key.Length + (p.Value?.Length ?? 0)) ?? 0)
        + (x.Source?.Headers?.Sum(p => (long)p.Key.Length + p.Value.Length) ?? 0)
        + (x.Origin?.Kind.Length ?? 0) + (x.Origin?.Label.Length ?? 0) + (x.Origin?.Id?.Length ?? 0);

    private void TrimHistory()
    {
        var length = history.Sum(TrackTextLength);
        var remove = 0;
        while (length > 32768 && remove < history.Length) length -= TrackTextLength(history[remove++]);
        if (remove > 0) history = history[remove..];
    }

    private void SetQueueItems(QueueTrack[] items)
    {
        Volatile.Write(ref queue, items);
        Volatile.Write(ref queueView, Array.AsReadOnly(items));
    }

    private async Task SendQueueAsync(QueueTrack[] items, string? current, RepeatMode mode)
    {
        var projected = ProjectNativeQueue(items, current);
        if (segmentLoop is { } loop && loop.ItemId == current && snapshot.Current is { } active)
        { projected = [active]; mode = RepeatMode.Off; }
        var prepared = await PrepareNativeQueueAsync(projected, current, mode).ConfigureAwait(false);
        var next = revision + 1;
        var payload = JsonSerializer.SerializeToElement(new NativeQueue(next, current,
            current is not null && !items.Any(x => x.ItemId == current) ? "off" : prepared.Repeat, prepared.Items), PlaybackJsonContext.Default.NativeQueue);
        var result = await native.CallAsync("queue.set", payload).ConfigureAwait(false);
        if (result.Long("queueRevision") != next) throw new IOException("Native 队列版本未确认");
        revision = next;
    }

    private QueueTrack[] ProjectNativeQueue(QueueTrack[] items, string? current)
    {
        var active = items.FirstOrDefault(x => x.ItemId == current)
            ?? (snapshot.Current?.ItemId == current ? snapshot.Current : null);
        if (!shuffle || repeat == RepeatMode.One)
            return active is not null && !items.Any(x => x.ItemId == current) ? [active, .. items] : items;
        var byId = items.Where(x => !x.QueuedNext).ToDictionary(x => x.ItemId, StringComparer.Ordinal);
        var bases = new List<QueueTrack>(shuffleOrder.Length);
        foreach (var id in shuffleOrder)
            if (byId.TryGetValue(id, out var track)) bases.Add(track);
        var ordered = shuffleOrder.ToHashSet(StringComparer.Ordinal);
        bases.AddRange(items.Where(x => !x.QueuedNext && !ordered.Contains(x.ItemId)));
        var pendingNext = items.Where(x => x.QueuedNext && x.ItemId != current).ToArray();
        var index = bases.FindIndex(x => x.ItemId == current);
        if (index >= 0) return [.. bases.Take(index), bases[index], .. pendingNext, .. bases.Skip(index + 1)];
        // A next-up occurrence occupies the gap in the base deck, then returns to it.
        var anchorIndex = Array.FindIndex(items, x => x.ItemId == current);
        var anchor = items.Take(Math.Max(0, anchorIndex)).LastOrDefault(x => !x.QueuedNext);
        var split = anchor is null ? 0 : Math.Max(0, bases.FindIndex(x => x.ItemId == anchor.ItemId) + 1);
        return [.. bases.Take(split), .. active is null ? Array.Empty<QueueTrack>() : new[] { active }, .. pendingNext, .. bases.Skip(split)];
    }

    private string[] CreateShuffleOrder(QueueTrack[] items, string? current)
        => QueuePlaybackPlan.Order(items, current, QueuePlaybackPlan.RecentTrackIds(history));

    private QueueTrack? ResolveNext(bool automatic = false)
    {
        if (automatic && repeat == RepeatMode.One && queue.Any(x => x.ItemId == snapshot.Current?.ItemId)) return snapshot.Current;
        if (queue.Length == 0) return null;
        var current = snapshot.Current?.ItemId;
        var projected = ProjectNativeQueue(queue, current);
        var steps = QueuePlaybackPlan.Upcoming(projected, current, repeat == RepeatMode.All, shuffle, QueuePlaybackPlan.RecentTrackIds(history), 1);
        return steps.Length == 0 ? null : FindProjected(projected, steps[0].ItemId);
    }

    private static QueueTrack? FindProjected(QueueTrack[] projected, string itemId)
    {
        foreach (var item in projected)
            if (item.ItemId == itemId) return item;
        return null;
    }

    private void PublishQueue()
    {
        var current = snapshot.Current?.ItemId;
        var projected = ProjectNativeQueue(queue, current);
        var steps = queue.Length == 0 || repeat == RepeatMode.One ? []
            : QueuePlaybackPlan.Upcoming(projected, current, repeat == RepeatMode.All, shuffle, QueuePlaybackPlan.RecentTrackIds(history));
        var state = new QueueSnapshot(++queueVersion, Queue, Array.AsReadOnly(history), current,
            steps.Length == 0 ? null : FindProjected(projected, steps[0].ItemId), QueueMode)
        { Plan = steps };
        Volatile.Write(ref queueSnapshot, state);
        PublishState();
        if (QueueChanged is { } handlers)
            foreach (Action<QueueSnapshot> handler in handlers.GetInvocationList()) try { handler(state); } catch (Exception) { }
    }
}
