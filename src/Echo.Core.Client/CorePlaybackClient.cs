using System.Text.Json;
using System.Runtime.InteropServices;
using Echo.Playback;
using Echo.Core.Contracts;
using Echo.Core.Transport;

namespace Echo.Core.Client;

public sealed partial class CorePlaybackClient : IPlaybackSession
{
    private readonly CoreWireClient wire;
    private readonly object gate = new();
    private readonly HashSet<PlaybackStateSubscription> subscribers = [];
    private CorePlaybackFrame frame;
    private bool closed;
    public PlaybackStateSnapshot State => Volatile.Read(ref frame).State;
    public PlaybackSnapshot Snapshot => State.Playback;
    public IReadOnlyList<QueueTrack> Queue => State.Queue.Items;
    public QueueSnapshot QueueState => State.Queue;
    public RepeatMode Repeat => State.Repeat;
    public QueuePlaybackMode QueueMode => QueueState.Mode;
    public JsonElement? RequestedOutput => State.Output.Requested;
    public bool GeneratedDsdAllowed => Volatile.Read(ref frame).GeneratedDsdAllowed;
    public bool VolumeLocked => Volatile.Read(ref frame).VolumeLocked;
    public bool CanUndoQueueEdit => Volatile.Read(ref frame).CanUndo;
    public string PlaybackRateMode => Volatile.Read(ref frame).PlaybackRateMode;
    public SegmentLoopState? SegmentLoop => Volatile.Read(ref frame).SegmentLoop;
    public event Action<PlaybackSnapshot>? Changed;
    public event Action<QueueSnapshot>? QueueChanged;
    public event Action<bool>? VolumeLockChanged;
    public event Action<SegmentLoopState?>? SegmentLoopChanged;

    internal CorePlaybackClient(CoreWireClient wire)
    {
        this.wire = wire;
        frame = wire.InitialPlayback ?? throw new InvalidDataException("Core 未提供初始播放状态。");
        wire.Received += Receive; wire.Disconnected += Disconnect;
    }

    private void Receive(CoreMessage message)
    {
        if (message.Playback is { } value) Apply(value);
    }

    internal void Apply(CorePlaybackFrame value)
    {
        CorePlaybackFrame previous;
        lock (gate)
        {
            if (closed || value.Serial <= frame.Serial && value.State.Generation == frame.State.Generation) return;
            previous = frame;
            if (!value.HasQueue) value = value with { State = value.State with { Queue = previous.State.Queue }, HasQueue = true };
            Volatile.Write(ref frame, value);
            var changes = Changes(previous.State, value.State);
            foreach (var subscriber in subscribers) subscriber.Offer(value.State, changes);
        }
        if (previous.State.Playback.Version != value.State.Playback.Version) Safe(Changed, value.State.Playback);
        if (previous.State.Queue.Version != value.State.Queue.Version) Safe(QueueChanged, value.State.Queue);
        if (previous.VolumeLocked != value.VolumeLocked) Safe(VolumeLockChanged, value.VolumeLocked);
        if (previous.SegmentLoop != value.SegmentLoop) Safe(SegmentLoopChanged, value.SegmentLoop);
    }

    private static PlaybackStateChanges Changes(PlaybackStateSnapshot previous, PlaybackStateSnapshot next)
    {
        if (previous.Generation != next.Generation || previous.IsAvailable != next.IsAvailable) return PlaybackStateChanges.All;
        var changes = PlaybackStateChanges.None;
        if (previous.Queue.Version != next.Queue.Version || previous.Repeat != next.Repeat) changes |= PlaybackStateChanges.Queue;
        var a = previous.Playback; var b = next.Playback;
        if (a.OperationId != b.OperationId || a.Status != b.Status || a.Current?.ItemId != b.Current?.ItemId || a.Volume != b.Volume || a.PlaybackRate != b.PlaybackRate || a.Error != b.Error || a.DurationSeconds != b.DurationSeconds)
            changes |= PlaybackStateChanges.Playback;
        if (a.PositionSeconds != b.PositionSeconds || a.OperationId != b.OperationId || a.Status != b.Status) changes |= PlaybackStateChanges.Clock;
        if (previous.Performance != next.Performance) changes |= PlaybackStateChanges.Performance;
        if (previous.Level?.Timestamp != next.Level?.Timestamp) changes |= PlaybackStateChanges.Meter;
        if (a.SampleRate != b.SampleRate || a.Channels != b.Channels || previous.Output.DeviceName != next.Output.DeviceName || previous.Output.HasConfirmedDevice != next.Output.HasConfirmedDevice
            || previous.Output.Backend != next.Output.Backend || !JsonSame(a.Processing, b.Processing) || !JsonSame(previous.Output.Requested, next.Output.Requested)) changes |= PlaybackStateChanges.Output;
        return changes;
    }
    private static bool JsonSame(JsonElement? a, JsonElement? b) => a.HasValue == b.HasValue && (!a.HasValue ||
        JsonMarshal.GetRawUtf8Value(a.Value).SequenceEqual(JsonMarshal.GetRawUtf8Value(b!.Value)));

    private void Disconnect(Exception _)
    {
        lock (gate)
        {
            if (closed) return;
            var state = frame.State with { IsAvailable = false,
                Output = frame.State.Output with { HasConfirmedDevice = false, Confirmed = null } };
            frame = frame with { State = state };
            foreach (var subscriber in subscribers) subscriber.Offer(state, PlaybackStateChanges.All);
        }
    }

    public PlaybackStateSubscription Subscribe(PlaybackStateChanges interests = PlaybackStateChanges.All)
    {
        if (interests == PlaybackStateChanges.None || (interests & ~PlaybackStateChanges.All) != 0) throw new ArgumentException("Invalid subscription categories.");
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (subscribers.Count >= 32) throw new InvalidOperationException("Playback supports at most 32 active controls.");
            var subscriber = new PlaybackStateSubscription(State, interests, value => { lock (gate) subscribers.Remove(value); });
            subscribers.Add(subscriber); return subscriber;
        }
    }

    private static void Safe<T>(Action<T>? callbacks, T value)
    {
        if (callbacks is null) return;
        foreach (Action<T> callback in callbacks.GetInvocationList()) try { callback(value); } catch (Exception) { }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closed = true;
            foreach (var subscriber in subscribers) subscriber.Complete();
            subscribers.Clear();
        }
        wire.Received -= Receive; wire.Disconnected -= Disconnect;
        return ValueTask.CompletedTask;
    }
}
