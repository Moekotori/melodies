using Echo.Playback;
namespace Echo.Core.Contracts;
public static class CorePlaybackFrames
{
    public static readonly QueueSnapshot EmptyQueue = new(0, [], [], null, null, QueuePlaybackMode.Sequential);
}
