using Echo.Contracts.Podcast;

namespace Echo.Application.Podcast;

public readonly record struct PodcastProgressSample(string Id, double Position, bool Ended);

// One latest sample from playback status. Completion comes only from Ended, never from duration.
public sealed class PodcastProgressTracker
{
    private readonly Func<PodcastProgressSample, Task> save;
    private readonly Func<long> clock;
    private readonly Dictionary<string, PodcastProgressSample> pending = new(StringComparer.Ordinal);
    private readonly List<string> order = [];
    private PodcastProgressSample? latest;
    private string lastState = "";
    private long lastSavedAt;
    private bool dirty;
    private Task? write;

    public PodcastProgressTracker(Func<PodcastProgressSample, Task> save, Func<long>? clock = null)
    {
        this.save = save;
        this.clock = clock ?? (() => Environment.TickCount64);
    }

    public void Observe(string? id, string state, double position)
    {
        var tracked = state is "playing" or "paused" or "ended";
        if (latest is { } previous && (previous.Id != id || !tracked))
        {
            _ = FlushAsync();
            latest = null;
        }
        if (id?.StartsWith("podcast:", StringComparison.Ordinal) != true || !tracked) return;
        if (!double.IsFinite(position) || position < 0) return;
        var ended = state == "ended";
        var changedTrack = latest?.Id != id;
        if (changedTrack || latest?.Position != position || latest?.Ended != ended)
        {
            latest = new(id, position, ended);
            dirty = true;
        }
        var changedState = lastState != state;
        lastState = state;
        if (changedTrack || changedState || clock() - lastSavedAt >= 15_000) _ = FlushAsync();
    }

    public Task FlushAsync()
    {
        if (latest is { } sample && dirty)
        {
            Remember(sample);
            dirty = false;
            lastSavedAt = clock();
        }
        // A synchronous drain finishes, then the assignment below stores that completed task.
        if (write is { IsCompleted: true }) write = null;
        if (write is not null) return write;
        if (pending.Count == 0) return Task.CompletedTask;
        lastSavedAt = clock();
        write = DrainAsync();
        return write;
    }

    public async Task DisposeAsync()
    {
        try { await FlushAsync().ConfigureAwait(false); }
        finally { latest = null; pending.Clear(); order.Clear(); }
    }

    private async Task DrainAsync()
    {
        try
        {
            while (pending.Count > 0 && order.Count > 0)
            {
                var id = order[0];
                order.RemoveAt(0);
                if (!pending.Remove(id, out var sample)) continue;
                try { await save(sample).ConfigureAwait(false); }
                catch (Exception)
                {
                    // One failed attempt per flush. The sample waits for the next pause, switch, or 15 second tick.
                    if (!pending.ContainsKey(id) && pending.Count < 8) { pending[id] = sample; order.Add(id); }
                    break;
                }
            }
        }
        finally { write = null; }
    }

    private void Remember(PodcastProgressSample sample)
    {
        if (!pending.ContainsKey(sample.Id) && pending.Count >= 8 && order.Count > 0)
        {
            pending.Remove(order[0]);
            order.RemoveAt(0);
        }
        if (!pending.ContainsKey(sample.Id)) order.Add(sample.Id);
        pending[sample.Id] = sample;
    }
}
