using System.Text.Json;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    public async Task<string> ExportCheckpointAsync()
    {
        string json = "";
        await lane.InvokeAsync(() =>
        {
            // Credential-bearing streams are deliberately excluded from disk state.
            var safe = queue.Where(x => x.Source?.Headers is not { Count: > 0 }).ToArray();
            var safeCurrent = snapshot.Current is { Source.Headers.Count: > 0 } ? null : snapshot.Current;
            var safeHistory = history.Where(x => x.Source?.Headers is not { Count: > 0 }).ToArray();
            var checkpoint = new PlaybackCheckpoint(2, safe, safeCurrent?.ItemId,
                repeat.ToString(), snapshot.PositionSeconds, snapshot.Volume, deviceConfiguration, new Dictionary<string, JsonElement>(dspCommands),
                shuffle, safeHistory, shuffleOrder, safe.Any(x => x.ItemId == safeCurrent?.ItemId) ? null : safeCurrent, shuffleHistory.ToArray());
            json = JsonSerializer.Serialize(checkpoint, PlaybackJsonContext.Default.PlaybackCheckpoint);
            if (json.Length > 512 * 1024) throw new IOException("播放状态超过保存上限");
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return json;
    }

    public Task RestoreCheckpointAsync(string json) => lane.InvokeAsync(() =>
    {
        if (snapshot.Version != 0 || disposed) return Task.CompletedTask; // New user actions win over late startup reads.
        if (json.Length > 512 * 1024) throw new IOException("播放状态超过读取上限");
        var state = JsonSerializer.Deserialize(json, PlaybackJsonContext.Default.PlaybackCheckpoint) ?? throw new IOException("无效的播放状态");
        if (state.SchemaVersion is not (1 or 2) || state.Items.Count > 1000 || state.Dsp.Count > 128
            || state.History is { Count: > 500 } || state.ShuffleOrder is { Count: > 1000 }
            || state.ShuffleHistory is { Count: > 100 } || state.ShuffleHistory?.Sum(id => (long)(id?.Length ?? 32769)) > 32768
            || !double.IsFinite(state.PositionSeconds) || state.PositionSeconds < 0 || !double.IsFinite(state.Volume) || state.Volume is < 0 or > 1
            || !Enum.TryParse<RepeatMode>(state.RepeatMode, out var mode) || !Enum.IsDefined(mode)
            || state.Items.Select(x => x.ItemId).Distinct().Count() != state.Items.Count)
            throw new IOException("不支持的播放状态");
        queue = state.Items.Where(x => x.Source?.Headers is not { Count: > 0 }).ToArray();
        ValidateItems(queue);
        Volatile.Write(ref queueView, Array.AsReadOnly(queue));
        repeat = mode; deviceConfiguration = state.Device;
        shuffle = state.Shuffle;
        history = state.History?.Where(x => x.Source?.Headers is not { Count: > 0 }).ToArray() ?? [];
        TrimHistory();
        foreach (var id in state.ShuffleHistory ?? history.Select(x => x.TrackId).TakeLast(100).ToArray()) RememberShuffleTrack(id);
        shuffleOrder = state.ShuffleOrder?.Where(id => queue.Any(x => x.ItemId == id)).Distinct().ToArray() ?? CreateShuffleOrder(queue, state.CurrentItemId);
        foreach (var entry in state.Dsp)
        {
            var colon = entry.Key.IndexOf(':');
            if (colon > 0 && IsDspMethod(entry.Key[..colon])) dspCommands[entry.Key] = entry.Value;
        }
        var current = queue.FirstOrDefault(x => x.ItemId == state.CurrentItemId)
            ?? (state.DetachedCurrent is { Source.Headers.Count: > 0 } ? null : state.DetachedCurrent);
        Publish(snapshot with { Current = current, PositionSeconds = current is null ? 0 : state.PositionSeconds, Volume = state.Volume, PlaybackRate = RestoredPlaybackRate(),
            Status = current is null ? PlaybackStatus.Stopped : PlaybackStatus.Paused });
        PublishQueue();
        return Task.CompletedTask;
    });
}
