using Echo.Core.Contracts;
using Echo.Playback;
namespace Echo.Core.Transport;

internal static class BridgePlaybackProjection
{
    public static CorePlaybackFrame? Apply(CorePlaybackFrame? frame, string role)
    {
        if (frame is null || role != "bridge") return frame;
        static QueueTrack? Track(QueueTrack? track) => track is null ? null : track with
        { Source = track.Source is { } source ? source with { Headers = null } : null };
        var current = Track(frame.State.Playback.Current);
        var queue = frame.State.Queue with { Items = current is null ? [] : [current], History = [], Upcoming = null, Plan = [] };
        var playback = frame.State.Playback with { Current = current, Error = null, Output = null, Processing = null };
        return frame with { State = frame.State with { Playback = playback, Queue = queue,
            Output = new("", null, false, false, null, null), Level = null }, PersistenceError = null, MediaError = null };
    }
}
