using System.Text.Json;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private sealed record PositionEvent(JsonElement Value, long Epoch);
    private long transportEpoch;
    private volatile bool suppressNativeEvents;
    private PositionEvent? latestPosition;
    private int positionQueued;
    private PositionEvent? latestPerformance;
    private int performanceQueued;

    private void OnNotification(string method, JsonElement value)
    {
        if (disposed || suppressNativeEvents) return;
        var epoch = Volatile.Read(ref transportEpoch);
        ObserveOutputConfirmation(method, value, epoch);
        if (method == "audio.levelMeter") { QueueLevel(value, epoch); return; }
        if (method == "host.performance")
        {
            Interlocked.Exchange(ref latestPerformance, new(value, epoch));
            if (Interlocked.Exchange(ref performanceQueued, 1) != 0) return;
            if (!lane.Post(() =>
            {
                Interlocked.Exchange(ref performanceQueued, 0);
                var metric = Interlocked.Exchange(ref latestPerformance, null);
                if (!disposed && metric is not null && metric.Epoch == transportEpoch) ObservePerformance(metric.Value, host: true);
                return Task.CompletedTask;
            })) Interlocked.Exchange(ref performanceQueued, 0);
            return;
        }
        if (method == "audio.position")
        {
            Interlocked.Exchange(ref latestPosition, new(value, epoch));
            if (Interlocked.Exchange(ref positionQueued, 1) != 0) return;
            if (lane.Post(async () =>
            {
                Interlocked.Exchange(ref positionQueued, 0);
                var position = Interlocked.Exchange(ref latestPosition, null);
                if (position is not null && position.Epoch == transportEpoch) await ApplyNotificationAsync(method, position.Value).ConfigureAwait(false);
            })) return;
            Interlocked.Exchange(ref positionQueued, 0);
            return; // Position is lossy; structural events are never silently dropped.
        }
        if (!lane.Post(async () =>
        {
            try { if (epoch == transportEpoch) await ApplyNotificationAsync(method, value).ConfigureAwait(false); }
            catch (Exception error) { Publish(snapshot with { Status = PlaybackStatus.Faulted, Error = error.Message }); }
        }))
            OnFailed(new IOException("播放事件通道已满，必须重新建立会话"));
    }

    private void OnFailed(Exception error)
    {
        if (suppressNativeEvents) return;
        var epoch = Volatile.Read(ref transportEpoch);
        _ = lane.InvokeAsync(() =>
        {
            if (epoch != transportEpoch) return Task.CompletedTask;
            initialized = false;
            Publish(snapshot with { Status = PlaybackStatus.Faulted, OperationId = 0, Error = error.Message });
            return Task.CompletedTask;
        }).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private async Task ApplyNotificationAsync(string method, JsonElement value)
    {
        if (disposed) return;
        if (method == "host.performance") { ObservePerformance(value, host: true); return; }
        if (method is "audio.deviceEvent" or "device.event")
        {
            if (value.Flag("recoveryRequired"))
                Publish(snapshot with { Status = PlaybackStatus.Faulted, OperationId = 0, Output = null, Error = "输出设备状态改变，请重新选择设备或重试播放" });
            return;
        }
        if (method == "audio.ended" && value.Flag("queueAdvance"))
        {
            // Native only publishes an advance once the next output becomes audible.
            if (value.Long("fromOperationId") != snapshot.OperationId || value.Long("queueRevision") != revision) return;
            var next = queue.FirstOrDefault(x => x.ItemId == value.Text("nextItemId"));
            if (next is null || value.Long("operationId") <= 0) return;
            var previous = snapshot.Current;
            CompleteTransition(previous, next);
            positionBase = value.Number("nextStartSeconds");
            Publish(snapshot with { Current = next, OperationId = value.Long("operationId"), Status = PlaybackStatus.Playing,
                PositionSeconds = positionBase, DurationSeconds = value.Number("nextDurationSeconds"),
                SampleRate = (int)value.Number("nextSampleRate", snapshot.SampleRate),
                Channels = (int)(snapshot.Output?.Long("channels", snapshot.Channels) ?? snapshot.Channels), Output = OutputFactsAfterAdvance(value) });
            if (previous is not null && (previous.QueuedNext || previous.RemoveAfterPlay) && previous.ItemId != next.ItemId)
                await SendQueueAsync(queue, next.ItemId, repeat).ConfigureAwait(false);
            return;
        }
        if (method.StartsWith("audio.", StringComparison.Ordinal) && value.Long("operationId") != snapshot.OperationId) return;
        switch (method)
        {
            case "audio.started":
                if (HasConfirmedRendering() && snapshot.Status is not (PlaybackStatus.Paused or PlaybackStatus.Stopped or PlaybackStatus.Faulted))
                    Publish(snapshot with { Status = PlaybackStatus.Playing });
                break;
            case "audio.position":
                if (snapshot.OperationId <= 0 || snapshot.Status is PlaybackStatus.Stopped or PlaybackStatus.Ended or PlaybackStatus.Faulted) return;
                ObservePerformance(value);
                var seconds = positionBase + value.Number("framesPlayed") / Math.Max(1, snapshot.SampleRate);
                Publish(snapshot with { PositionSeconds = snapshot.DurationSeconds > 0 ? Math.Min(seconds, snapshot.DurationSeconds) : seconds,
                    Status = snapshot.Status == PlaybackStatus.Loading && HasConfirmedRendering() ? PlaybackStatus.Playing : snapshot.Status,
                    Processing = value.Child("processing") ?? snapshot.Processing });
                await ApplySegmentBoundaryAsync().ConfigureAwait(false);
                break;
            case "audio.ended":
                await HandleNativeEofAsync(value.Long("operationId")).ConfigureAwait(false);
                break;
            case "audio.error":
                if (IsNativeAdvanceFailure(value.Text("message"))) nativeAdvanceFallbackOperation = snapshot.OperationId;
                Publish(snapshot with { Status = PlaybackStatus.Faulted, Error = value.Text("message") ?? "原生播放失败" });
                break;
            case "device.event":
                Publish(snapshot with { Output = value });
                break;
        }
    }
}
