using Echo.Contracts.Tasks;
using Echo.Playback;
using System.Threading.Channels;

namespace Echo.Application.Scheduling;

public sealed class PlaybackWorkAdmission : IBackgroundWorkAdmission, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IPlaybackSession session;
    private readonly TimeProvider time;
    private readonly PlaybackWorkPolicy policy;
    private readonly PlaybackStateSubscription subscription;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task observer;
    private readonly SortedSet<long> metadataTickets = [];
    private TaskCompletionSource changed = NewSignal();
    private BackgroundAdmissionSnapshot snapshot = new(PlaybackWorkPressure.Normal, PlaybackWorkReason.None, 4, 256, 0, 0);
    private int active, waiting;
    private long nextTicket;
    private bool closed;

    public PlaybackWorkAdmission(IPlaybackSession session, TimeProvider? time = null)
    {
        this.session = session; this.time = time ?? TimeProvider.System;
        policy = new(this.time);
        subscription = session.Subscribe(PlaybackStateChanges.Playback | PlaybackStateChanges.Performance);
        observer = Task.Run(ObserveAsync);
    }

    public BackgroundAdmissionSnapshot Snapshot
    { get { lock (gate) { Refresh(); return snapshot with { ActiveMetadata = active, WaitingRequests = waiting }; } } }

    private void Refresh()
    {
        var next = policy.Observe(session.State, time.GetTimestamp());
        if (snapshot.Pressure != next.Pressure || snapshot.MetadataConcurrency != next.MetadataConcurrency) Pulse();
        snapshot = next;
    }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void Pulse()
    {
        if (waiting == 0) return;
        var prior = changed; changed = NewSignal(); prior.TrySetResult();
    }
    private async Task ObserveAsync()
    {
        try { while (true) { await subscription.ReadAsync(lifetime.Token).ConfigureAwait(false); lock (gate) Refresh(); } }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (ChannelClosedException) { }
    }

    public async ValueTask<IDisposable> EnterMetadataAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        long ticket;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this); Refresh();
            if (metadataTickets.Count == 0 && active < snapshot.MetadataConcurrency) { active++; return new Lease(this); }
            if (waiting >= 32) throw new InvalidOperationException("Background admission is busy.");
            ticket = ++nextTicket; metadataTickets.Add(ticket); waiting++;
        }
        try
        {
            while (true)
            {
                Task wake;
                lock (gate)
                {
                    cancellation.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(closed, this); Refresh();
                    if (metadataTickets.Min == ticket && active < snapshot.MetadataConcurrency)
                    { metadataTickets.Remove(ticket); active++; Pulse(); return new Lease(this); }
                    wake = changed.Task;
                }
                await Wait(wake, cancellation).ConfigureAwait(false);
            }
        }
        finally { lock (gate) { metadataTickets.Remove(ticket); waiting--; Pulse(); } }
    }

    public async ValueTask WaitAsync(BackgroundWorkKind kind, CancellationToken cancellation)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid background work kind.");
        cancellation.ThrowIfCancellationRequested();
        if (kind == BackgroundWorkKind.VisibleArtwork)
        {
            lock (gate) ObjectDisposedException.ThrowIf(closed, this);
            var current = Snapshot;
            // Cache hits bypass this gate. Visible misses remain bounded and can continue under protection.
            if (current.Pressure == PlaybackWorkPressure.Protected) await Task.Delay(100, cancellation).ConfigureAwait(false);
            return;
        }
        var registered = false;
        try
        {
            while (true)
            {
                Task wake;
                lock (gate)
                {
                    cancellation.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(closed, this); Refresh();
                    if (snapshot.Pressure != PlaybackWorkPressure.Protected) return;
                    if (!registered)
                    {
                        if (waiting >= 32) throw new InvalidOperationException("Background admission is busy.");
                        waiting++; registered = true;
                    }
                    wake = changed.Task;
                }
                await Wait(wake, cancellation).ConfigureAwait(false);
            }
        }
        finally { if (registered) lock (gate) { waiting--; Pulse(); } }
    }
    private static async Task Wait(Task wake, CancellationToken cancellation)
    {
        try { await wake.WaitAsync(TimeSpan.FromMilliseconds(250), cancellation).ConfigureAwait(false); }
        catch (TimeoutException) { /* Re-evaluate telemetry expiry; no idle polling task is kept alive. */ }
    }
    private void Release() { lock (gate) { active--; Pulse(); } }
    public async ValueTask DisposeAsync()
    {
        lock (gate) { if (closed) return; closed = true; Pulse(); }
        subscription.Dispose(); await lifetime.CancelAsync().ConfigureAwait(false);
        await observer.ConfigureAwait(false); lifetime.Dispose();
    }
    private sealed class Lease(PlaybackWorkAdmission owner) : IDisposable
    {
        private PlaybackWorkAdmission? current = owner;
        public void Dispose() => Interlocked.Exchange(ref current, null)?.Release();
    }
}
