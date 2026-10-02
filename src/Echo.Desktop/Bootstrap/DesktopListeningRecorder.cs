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
            await ObserveAsync(subscription.Initial.Playback).ConfigureAwait(false);
            await SaveAsync().ConfigureAwait(false);
            while (true)
            {
                var update = await subscription.ReadAsync(stop.Token).ConfigureAwait(false);
                var state = update.State.Playback;
                var same = previous?.Current?.ItemId == state.Current?.ItemId
                    && !(previous?.OperationId > 0 && state.OperationId == 0)
                    && !(previous?.Status == PlaybackStatus.Ended && state.Status == PlaybackStatus.Loading);
                Accumulate(state, same);
                if (!same || state.Status is PlaybackStatus.Ended or PlaybackStatus.Stopped or PlaybackStatus.Faulted)
                {
                    if (entry is not null) { entry = entry with { EndedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }; dirty = true; }
                    await SaveAsync().ConfigureAwait(false); entry = null;
                }
                var starting = entry is null && state.Status == PlaybackStatus.Playing;
                var leavingPlaying = previous?.Status == PlaybackStatus.Playing && state.Status != PlaybackStatus.Playing;
                await ObserveAsync(state).ConfigureAwait(false);
                if (starting || leavingPlaying || Stopwatch.GetElapsedTime(savedAt).TotalSeconds >= 15)
                    await SaveAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (System.Threading.Channels.ChannelClosedException) { }
        finally
        {
            if (entry is not null) { entry = entry with { EndedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }; dirty = true; }
            await SaveAsync().ConfigureAwait(false);
        }
    }
    private void Accumulate(PlaybackSnapshot next, bool same)
    {
        if (entry is null || previous?.Status != PlaybackStatus.Playing) return;
        var elapsed = Math.Clamp(Stopwatch.GetElapsedTime(sampledAt).TotalSeconds, 0, 30);
        var delta = same ? next.PositionSeconds - previous.PositionSeconds : elapsed;
        // A seek awards none of the skipped duration. Speed affects audio progress, not wall listening time.
        if (delta <= 0 || delta > elapsed * 4 + 1) return;
        entry = entry with { PlayedSeconds = entry.PlayedSeconds + elapsed, UpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        dirty = true;
    }
    private async Task ObserveAsync(PlaybackSnapshot state)
    {
        var now = Stopwatch.GetTimestamp();
        if (entry is null && state is { Status: PlaybackStatus.Playing, OperationId: > 0, Current: { } track })
        {
            var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var epoch = library is IHistoryLibrary history ? await history.GetHistoryEpochAsync(stop.Token).ConfigureAwait(false) : (long?)null;
            entry = new(Guid.NewGuid().ToString("N"), track.TrackId, track.FilePath, unix, unix, 0,
                Source: track.Origin?.Label ?? "", HistoryEpoch: epoch);
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
