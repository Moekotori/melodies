namespace Echo.Playback;
public interface IPlaybackFileResolver
{
    Task<IReadOnlyList<QueueTrack>> ResolveAsync(IEnumerable<string> paths, CancellationToken token);
}
