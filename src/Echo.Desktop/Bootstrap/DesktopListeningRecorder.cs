using System.Diagnostics;
using Echo.Contracts.Library;
using Echo.Playback;

namespace Echo.Desktop.Bootstrap;

// One subscription and one awaited write; the recorder outlives the home page.
internal sealed class DesktopListeningRecorder : IAsyncDisposable
{
    private readonly IHomeLibrary library;
    private readonly Action changed;
    private readonly Action<string> report;
    private readonly PlaybackStateSubscription subscription;
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private ListeningEntry? entry;
    private PlaybackSnapshot? previous;
    private long sampledAt, savedAt;
    private bool dirty;

    public DesktopListeningRecorder(PlaybackZone zone, IHomeLibrary library, Action changed, Action<string> report)
    {
        this.library = library; this.changed = changed; this.report = report;
        subscription = zone.Subscribe(PlaybackStateChanges.Playback | PlaybackStateChanges.Clock);
        worker = Task.Run(RunAsync);
    }
    private async Task RunAsync()
    {
        try
        {
            Observe(subscription.Initial.Playback);
            while (true)
            {
                var update = await subscription.ReadAsync(stop.Token).ConfigureAwait(false);
                var state = update.State.Playback;
                var same = previous?.Current?.ItemId == state.Current?.ItemId && previous?.OperationId == state.OperationId;
                Accumulate(state, same);
                if (!same) { await SaveAsync().ConfigureAwait(false); entry = null; }
                var starting = entry is null && state.Status == PlaybackStatus.Playing;
                var leavingPlaying = previous?.Status == PlaybackStatus.Playing && state.Status != PlaybackStatus.Playing;
                Observe(state);
                if (starting || leavingPlaying || Stopwatch.GetElapsedTime(savedAt).TotalSeconds >= 15)
                    await SaveAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (System.Threading.Channels.ChannelClosedException) { }
        finally { await SaveAsync().ConfigureAwait(false); }
    }
    private void Accumulate(PlaybackSnapshot next, bool same)
    {
        if (entry is null || previous?.Status != PlaybackStatus.Playing || !same) return;
        var elapsed = Math.Clamp(Stopwatch.GetElapsedTime(sampledAt).TotalSeconds, 0, 30);
        var delta = next.PositionSeconds - previous.PositionSeconds;
        // A seek awards none of the skipped duration. Speed affects audio progress, not wall listening time.
        if (delta <= 0 || delta > elapsed * 4 + 1) return;
        entry = entry with { PlayedSeconds = entry.PlayedSeconds + Math.Min(elapsed, delta), UpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        dirty = true;
    }
    private void Observe(PlaybackSnapshot state)
    {
        var now = Stopwatch.GetTimestamp();
        if (entry is null && state is { Status: PlaybackStatus.Playing, OperationId: > 0, Current: { } track })
        {
            var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            entry = new(Guid.NewGuid().ToString("N"), track.TrackId, track.FilePath, unix, unix, 0);
            savedAt = now; dirty = true;
        }
        previous = state; sampledAt = now;
    }
    private async Task SaveAsync()
    {
        if (!dirty || entry is null) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await library.RecordListeningAsync(entry, timeout.Token).ConfigureAwait(false);
            dirty = false; changed();
        }
        catch (Exception) { report("听歌记录暂未保存，请检查曲库后重试。"); }
        finally { savedAt = Stopwatch.GetTimestamp(); }
    }
    public async ValueTask DisposeAsync()
    { stop.Cancel(); subscription.Dispose(); await worker.ConfigureAwait(false); stop.Dispose(); }
}
