using System.Text.Json;

namespace Echo.Playback;

public interface IPlaybackSession : IAsyncDisposable
{
    PlaybackSnapshot Snapshot { get; }
    PlaybackStateSnapshot State { get; }
    IReadOnlyList<QueueTrack> Queue { get; }
    QueueSnapshot QueueState { get; }
    QueuePlaybackMode QueueMode { get; }
    RepeatMode Repeat { get; }
    JsonElement? RequestedOutput { get; }
    bool GeneratedDsdAllowed { get; }
    bool VolumeLocked { get; }
    bool CanUndoQueueEdit { get; }
    string PlaybackRateMode { get; }
    SegmentLoopState? SegmentLoop { get; }
    event Action<PlaybackSnapshot>? Changed;
    event Action<QueueSnapshot>? QueueChanged;
    event Action<bool>? VolumeLockChanged;
    event Action<SegmentLoopState?>? SegmentLoopChanged;
    PlaybackStateSubscription Subscribe(PlaybackStateChanges interests = PlaybackStateChanges.All);
    Task<IReadOnlyList<QueueTrack>> ResolveFilesAsync(IEnumerable<string> paths, CancellationToken token = default);
    Task SetVolumeAsync(double volume, CancellationToken token = default);
    Task<JsonElement> GetDevicesAsync(CancellationToken token = default);
    Task ConfigureOutputAsync(JsonElement configuration, CancellationToken token = default);
    Task<JsonElement> DspAsync(string method, JsonElement parameters, bool retainForRecovery = false, CancellationToken token = default);
    Task<JsonElement?> GetRetainedDspRequestAsync(string method, CancellationToken token = default);
    Task RecoverAsync(CancellationToken token = default);
    Task SetAutomixEnabledAsync(bool enabled, CancellationToken token = default);
    Task<string> ExportCheckpointAsync();
    Task RestoreCheckpointAsync(string json);
    Task SetAlbumGaplessEnabledAsync(bool enabled, CancellationToken token = default);
    Task SetShuffleAvoidRecentCountAsync(int count, CancellationToken token = default);
    Task<IReadOnlyList<AudioOutputMode>> GetOutputModesAsync(CancellationToken token = default);
    Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync(string mode, CancellationToken token = default);
    Task<JsonElement> ProbeAsioDsdAsync(AudioOutputDevice device, CancellationToken token = default);
    Task ConfigureOutputAsync(AudioOutputConfiguration configuration, CancellationToken token = default);
    Task SetVolumeLockedAsync(bool locked, CancellationToken token = default);
    Task SetPlaybackRateModeAsync(string mode, CancellationToken token = default);
    Task PlayAsync(QueueTrack track, CancellationToken token = default);
    Task SetQueueAsync(IEnumerable<QueueTrack> tracks, string? startItemId = null, CancellationToken token = default);
    Task EnqueueAsync(IEnumerable<QueueTrack> tracks, bool nextUp = false, CancellationToken token = default);
    Task RemoveQueueItemsAsync(IEnumerable<string> ids, CancellationToken token = default);
    Task ClearQueueAsync(CancellationToken token = default);
    Task MoveQueueItemsAsync(IEnumerable<string> ids, int targetIndex, CancellationToken token = default);
    Task MoveQueueItemsNextAsync(IEnumerable<string> ids, CancellationToken token = default);
    Task SetRemoveAfterPlayAsync(IEnumerable<string> ids, bool enabled, CancellationToken token = default);
    Task UndoQueueEditAsync(CancellationToken token = default);
    Task SelectQueueItemAsync(string itemId, CancellationToken token = default);
    Task NextAsync(CancellationToken token = default);
    Task PreviousAsync(CancellationToken token = default);
    Task SetQueueModeAsync(QueuePlaybackMode mode, CancellationToken token = default);
    Task SetRepeatAsync(RepeatMode mode, CancellationToken token = default);
    Task SetPlaybackRateAsync(double rate, CancellationToken token = default);
    Task SetSegmentLoopAsync(string itemId, double start, double end, CancellationToken token = default);
    Task ClearSegmentLoopAsync(CancellationToken token = default);
    Task PauseAsync(CancellationToken token = default);
    Task ResumeAsync(CancellationToken token = default);
    Task SeekAsync(double seconds, CancellationToken token = default);
    Task StopAsync(CancellationToken token = default);
}
