using System.Diagnostics;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private readonly object publicationGate = new();
    private readonly HashSet<PlaybackStateSubscription> subscriptions = [];
    private readonly Guid stateGeneration = Guid.NewGuid();
    private PlaybackStateSnapshot? publishedState;
    private bool publicationClosed;
    public PlaybackStateSnapshot State => Volatile.Read(ref publishedState)!;

    private void InitializeState() => publishedState = new(stateGeneration, 0, snapshot, queueSnapshot,
        PlaybackOutputState.From(snapshot, deviceConfiguration), Stopwatch.GetTimestamp(), repeat, true);

    public PlaybackStateSubscription Subscribe(PlaybackStateChanges interests = PlaybackStateChanges.All)
    {
        if (interests == PlaybackStateChanges.None || (interests & ~PlaybackStateChanges.All) != 0)
            throw new ArgumentException("Invalid subscription categories.", nameof(interests));
        lock (publicationGate)
        {
            ObjectDisposedException.ThrowIf(publicationClosed || Volatile.Read(ref disposed), this);
            if (subscriptions.Count >= 32) throw new InvalidOperationException("Playback supports at most 32 active controls.");
            var subscription = new PlaybackStateSubscription(State, interests, RemoveSubscription);
            subscriptions.Add(subscription);
            return subscription;
        }
    }

    private void RemoveSubscription(PlaybackStateSubscription subscription)
    { lock (publicationGate) subscriptions.Remove(subscription); }

    private void PublishState()
    {
        var prior = State;
        var changes = PlaybackStateChanges.None;
        if (!ReferenceEquals(prior.Queue, queueSnapshot) || prior.Repeat != repeat) changes |= PlaybackStateChanges.Queue;
        var previous = prior.Playback;
        if (prior.IsAvailable != !disposed || previous.Current != snapshot.Current || previous.Status != snapshot.Status || previous.OperationId != snapshot.OperationId
            || previous.DurationSeconds != snapshot.DurationSeconds || previous.Volume != snapshot.Volume || previous.Error != snapshot.Error)
            changes |= PlaybackStateChanges.Playback;
        if (previous.PositionSeconds != snapshot.PositionSeconds || previous.Status != snapshot.Status
            || previous.OperationId != snapshot.OperationId) changes |= PlaybackStateChanges.Clock;
        var output = PlaybackOutputState.From(snapshot, deviceConfiguration);
        if (prior.Output != output || previous.SampleRate != snapshot.SampleRate || previous.Channels != snapshot.Channels
            || !Nullable.Equals(previous.Processing, snapshot.Processing)) changes |= PlaybackStateChanges.Output;
        if (changes == PlaybackStateChanges.None) return;
        var timestamp = (changes & PlaybackStateChanges.Clock) != 0 ? Stopwatch.GetTimestamp() : prior.ClockTimestamp;
        var state = new PlaybackStateSnapshot(stateGeneration, prior.Version + 1, snapshot, queueSnapshot, output, timestamp, repeat, !disposed);
        lock (publicationGate)
        {
            Volatile.Write(ref publishedState, state);
            foreach (var subscription in subscriptions) subscription.Offer(state, changes);
        }
    }

    private void CloseStateSubscriptions()
    {
        lock (publicationGate)
        {
            publicationClosed = true;
            foreach (var subscription in subscriptions) subscription.Complete();
            subscriptions.Clear();
        }
    }
}
