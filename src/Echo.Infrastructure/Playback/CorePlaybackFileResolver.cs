using Echo.Playback;
namespace Echo.Infrastructure.Playback;
public sealed class CorePlaybackFileResolver : IPlaybackFileResolver
{
    public Task<IReadOnlyList<QueueTrack>> ResolveAsync(IEnumerable<string> paths, CancellationToken token)
        => PlaybackFileResolver.ResolveAsync(paths, token);
}
