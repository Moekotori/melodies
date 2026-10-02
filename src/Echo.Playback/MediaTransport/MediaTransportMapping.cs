namespace Echo.Playback.MediaTransport;

public static class MediaTransportMapping
{
    public static MediaTransportState Create(PlaybackSnapshot state, IReadOnlyList<QueueTrack> queue, RepeatMode repeat, QueueSnapshot? queueState = null)
    {
        var track = state.Current;
        string Text(string key, string fallback = "")
        {
            var value = track?.Metadata?.GetValueOrDefault(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value[..Math.Min(value.Length, 2048)];
        }
        var index = -1;
        for (var i = 0; i < queue.Count; i++) if (queue[i].ItemId == track?.ItemId) { index = i; break; }
        var playable = track is not null && (index >= 0 || queueState is not null) && state.Status != PlaybackStatus.Faulted;
        var active = playable && state.OperationId > 0;
        var duration = Text("isLiveStream") == "true" ? 0 : Safe(state.DurationSeconds);
        var position = Safe(state.PositionSeconds);
        if (state.Status == PlaybackStatus.Loading && state.OperationId == 0) { duration = 0; position = 0; }
        if (duration > 0) position = Math.Min(position, duration);
        var cover = Text("coverPath");
        if (!Path.IsPathFullyQualified(cover)) cover = "";
        return new(playable ? track?.ItemId : null, Text("title", track is null ? "ECHO" : Path.GetFileNameWithoutExtension(track.FilePath)),
            Text("artist"), Text("album"), Text("albumArtist"), cover.Length == 0 ? null : cover,
            state.Status switch { PlaybackStatus.Playing => "playing", PlaybackStatus.Paused => "paused", PlaybackStatus.Loading => "loading", _ => "stopped" },
            position, duration, playable, active && state.Status is PlaybackStatus.Playing or PlaybackStatus.Loading,
            playable && (queueState is null ? queue.Count > 0 : queueState.History.Count > 0 || track?.Origin?.Kind == "album" && index > 0),
            playable && (queueState is null || repeat == RepeatMode.One ? queue.Count > 0 && (index < queue.Count - 1 || repeat == RepeatMode.All) : queueState.Upcoming is not null),
            active && duration > 0, repeat switch { RepeatMode.One => "track", RepeatMode.All => "list", _ => "none" });
    }
    private static double Safe(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 31_536_000) : 0;
}
