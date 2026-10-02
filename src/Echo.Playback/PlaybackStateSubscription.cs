using System.Threading.Channels;

namespace Echo.Playback;

/// <summary>Single-reader latest-state subscription. Pending change categories are merged instead of lost.</summary>
public sealed class PlaybackStateSubscription : IDisposable
{
    private readonly object gate = new();
    private readonly Channel<bool> signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Action<PlaybackStateSubscription> remove;
    private PlaybackStateUpdate? pending;
    private bool closed;
    private int reading;
    internal PlaybackStateChanges Interests { get; }
    public PlaybackStateSnapshot Initial { get; }

    internal PlaybackStateSubscription(PlaybackStateSnapshot initial, PlaybackStateChanges interests,
        Action<PlaybackStateSubscription> remove)
    {
        Initial = initial; Interests = interests; this.remove = remove;
        Offer(initial, PlaybackStateChanges.All, true);
    }

    internal void Offer(PlaybackStateSnapshot state, PlaybackStateChanges changes, bool initial = false)
    {
        changes &= Interests;
        if (!initial && changes == PlaybackStateChanges.None) return;
        lock (gate)
        {
            if (closed) return;
            pending = new(state, changes | (pending?.Changes ?? PlaybackStateChanges.None), initial || pending?.IsInitial == true);
            signal.Writer.TryWrite(true);
        }
    }

    public async Task<PlaybackStateUpdate> ReadAsync(CancellationToken token = default)
    {
        if (Interlocked.Exchange(ref reading, 1) != 0) throw new InvalidOperationException("A subscription supports one reader.");
        try
        {
            while (true)
            {
                await signal.Reader.ReadAsync(token).ConfigureAwait(false);
                lock (gate)
                {
                    if (pending is not { } update) continue;
                    pending = null;
                    return update;
                }
            }
        }
        finally { Volatile.Write(ref reading, 0); }
    }

    internal void Complete()
    {
        lock (gate) { closed = true; signal.Writer.TryComplete(); }
    }

    public void Dispose()
    {
        lock (gate) { closed = true; pending = null; signal.Writer.TryComplete(); }
        remove(this);
    }
}
