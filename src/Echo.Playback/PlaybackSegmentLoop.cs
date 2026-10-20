using System.Text.Json.Nodes;

namespace Echo.Playback;


public sealed partial class PlaybackSessionCoordinator
{
    private SegmentLoopState? segmentLoop;
    public SegmentLoopState? SegmentLoop => Volatile.Read(ref segmentLoop);
    public event Action<SegmentLoopState?>? SegmentLoopChanged;

    public Task SetSegmentLoopAsync(string itemId, double start, double end, CancellationToken token = default)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end - start < 0.5)
            throw new ArgumentException("片段长度至少需要 0.5 秒，且 A 必须早于 B。");
        return Command(async () =>
        {
            if (snapshot.Current?.ItemId != itemId || snapshot.OperationId <= 0 || snapshot.DurationSeconds <= 0
                || snapshot.Current.Metadata?.GetValueOrDefault("isLiveStream") == "true"
                || snapshot.Status is PlaybackStatus.Stopped or PlaybackStatus.Ended or PlaybackStatus.Faulted)
                throw new InvalidOperationException("请先播放一首可定位的本地或网盘歌曲。");
            if (end > snapshot.DurationSeconds - 0.25)
                throw new ArgumentException("B 点需在曲目结束前至少 0.25 秒，避免自动切歌。");
            var desired = new SegmentLoopState(itemId, start, end);
            var prior = segmentLoop;
            // Disable native next-track prefetch before seeking. The persisted/UI queue is unchanged.
            Volatile.Write(ref segmentLoop, desired);
            try
            {
                await SendQueueAsync(queue, itemId, repeat).ConfigureAwait(false);
                await SeekNowAsync(start).ConfigureAwait(false);
            }
            catch
            {
                Volatile.Write(ref segmentLoop, prior);
                try { await SendQueueAsync(queue, itemId, repeat).ConfigureAwait(false); }
                catch { SetSegmentLoop(null); }
                throw;
            }
            SetSegmentLoop(desired);
        }, token, affectsPlayback: false);
    }

    public Task ClearSegmentLoopAsync(CancellationToken token = default)
        => Command(ClearSegmentLoopNowAsync, token, affectsPlayback: false);

    private async Task ClearSegmentLoopNowAsync()
    {
        if (segmentLoop is null) return;
        var prior = segmentLoop;
        Volatile.Write(ref segmentLoop, null);
        try { if (initialized) await SendQueueAsync(queue, snapshot.Current?.ItemId, repeat).ConfigureAwait(false); }
        catch { Volatile.Write(ref segmentLoop, prior); throw; }
        SetSegmentLoop(null);
        await RefreshAlbumGaplessAsync().ConfigureAwait(false);
    }

    private void SetSegmentLoop(SegmentLoopState? value)
    {
        Volatile.Write(ref segmentLoop, value);
        if (SegmentLoopChanged is { } handlers)
            foreach (Action<SegmentLoopState?> handler in handlers.GetInvocationList())
                try { handler(value); } catch { /* Views cannot interrupt playback. */ }
    }

    private async Task SeekNowAsync(double position)
    {
        var result = await native.CallAsync("audio.seek", OperationParams(new JsonObject { ["positionSeconds"] = position })).ConfigureAwait(false);
        var operation = result.Long("operationId");
        if (operation <= 0) throw new IOException("Native 未确认定位请求。");
        gaplessContinuation = false;
        gaplessChain = [];
        positionBase = result.Number("positionSeconds", position);
        ArmOutputConfirmation(operation);
        Publish(snapshot with { OperationId = operation, PositionSeconds = positionBase });
        await SendQueueAsync(queue, snapshot.Current?.ItemId, repeat).ConfigureAwait(false);
        if (snapshot.Current is { } track) await TryPrepareAlbumGaplessAsync(track).ConfigureAwait(false);
    }

    private async Task ApplySegmentBoundaryAsync()
    {
        if (segmentLoop is not { } loop || snapshot.Status != PlaybackStatus.Playing
            || snapshot.Current?.ItemId != loop.ItemId || snapshot.PositionSeconds < loop.EndSeconds) return;
        try { await SeekNowAsync(loop.StartSeconds).ConfigureAwait(false); }
        catch (Exception error)
        {
            SetSegmentLoop(null);
            Publish(snapshot with { Status = PlaybackStatus.Faulted, Error = error.Message });
        }
    }
}
