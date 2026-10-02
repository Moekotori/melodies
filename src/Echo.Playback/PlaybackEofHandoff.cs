namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private (long Epoch, long Operation)? handledEof;
    private long pausedEofOperation;
    private long nativeAdvanceFallbackOperation;
    private static bool IsNativeAdvanceFailure(string? message) => message is not null && (
        message.StartsWith("daemon_auto_advance_", StringComparison.Ordinal)
        || message.StartsWith("asio_pcm_sample_rate_transition_", StringComparison.Ordinal));

    private async Task HandleNativeEofAsync(long operation)
    {
        if (operation <= 0 || operation != snapshot.OperationId || snapshot.Status is PlaybackStatus.Stopped or PlaybackStatus.Ended) return;
        if (snapshot.Status == PlaybackStatus.Paused) { pausedEofOperation = operation; return; }
        if (snapshot.Status == PlaybackStatus.Faulted && nativeAdvanceFallbackOperation != operation) return;
        var identity = (transportEpoch, operation);
        if (handledEof == identity) return;
        handledEof = identity;
        pausedEofOperation = 0; nativeAdvanceFallbackOperation = 0;
        if (segmentLoop is { } loop && loop.ItemId == snapshot.Current?.ItemId)
        {
            Publish(snapshot with { Status = PlaybackStatus.Playing, PositionSeconds = loop.EndSeconds });
            await ApplySegmentBoundaryAsync().ConfigureAwait(false);
            return;
        }
        if (snapshot.Current?.Metadata?.GetValueOrDefault("isLiveStream") != "true" && ResolveNext(automatic: true) is { } next)
        {
            await SwitchToAsync(next).ConfigureAwait(false);
            return;
        }
        if (snapshot.Current is { RemoveAfterPlay: true } completed)
        {
            SetQueueItems(queue.Where(x => x.ItemId != completed.ItemId).ToArray());
            history = history.Where(x => x.ItemId != completed.ItemId).ToArray(); undo = null;
            await SendQueueAsync(queue, completed.ItemId, repeat).ConfigureAwait(false);
            PublishQueue();
        }
        Publish(snapshot with { Status = PlaybackStatus.Ended, PositionSeconds = snapshot.DurationSeconds });
    }
}
