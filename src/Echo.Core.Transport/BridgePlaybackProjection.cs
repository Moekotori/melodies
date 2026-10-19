using Echo.Core.Contracts;
using Echo.Playback;
namespace Echo.Core.Transport;

internal static class BridgePlaybackProjection
{
    public static CorePlaybackFrame? Apply(CorePlaybackFrame? frame, string role)
    {
        if (frame is null) return null;
        if (role == "remote-control") return Remote(frame);
        if (role != "bridge") return frame;
        static QueueTrack? Track(QueueTrack? track) => track is null ? null : track with
        { Source = track.Source is { } source ? source with { Headers = null } : null };
        var current = Track(frame.State.Playback.Current);
        var queue = frame.State.Queue with { Items = current is null ? [] : [current], History = [], Upcoming = null, Plan = [] };
        var playback = frame.State.Playback with { Current = current, Error = null, Output = null, Processing = null };
        return frame with { State = frame.State with { Playback = playback, Queue = queue,
            Output = new("", null, false, false, null, null), Level = null }, PersistenceError = null, MediaError = null };
    }
    private static CorePlaybackFrame Remote(CorePlaybackFrame frame)
    {
        static QueueTrack? Safe(QueueTrack? track)
        {
            if (track is null) return null;
            var streaming = track.Source?.Kind == "http" || track.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            return track with { Source = null, FilePath = streaming ? "stream://current" : track.FilePath,
                TrackId = streaming ? "stream" : track.TrackId,
                Origin = null, Metadata = track.Metadata?.Where(pair => pair.Key is "title" or "artist" or "album" or "albumArtist").ToDictionary(pair => pair.Key, pair => pair.Value) };
        }
        var queue = frame.State.Queue with { Items = frame.State.Queue.Items.Select(track => Safe(track)!).ToArray(),
            History = frame.State.Queue.History.Select(track => Safe(track)!).ToArray(), Upcoming = Safe(frame.State.Queue.Upcoming) };
        return frame with { State = frame.State with { Queue = queue,
            Playback = frame.State.Playback with { Current = Safe(frame.State.Playback.Current), Error = null, Output = null, Processing = null },
            Output = new("", null, false, false, null, null), Level = null, Performance = PlaybackPerformance.Unknown }, PersistenceError = null, MediaError = null };
    }
}
