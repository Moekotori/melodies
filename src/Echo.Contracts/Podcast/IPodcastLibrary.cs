namespace Echo.Contracts.Podcast;

// Subscriptions and episode metadata. Playback stays on the shared session.
public interface IPodcastLibrary : IDisposable
{
    Task<IReadOnlyList<PodcastFeed>> ListAsync(CancellationToken token);
    Task<PodcastFeed> SubscribeAsync(string url, CancellationToken token);
    Task<PodcastFeed> RefreshAsync(string feedId, CancellationToken token);
    Task UnsubscribeAsync(string feedId, CancellationToken token);
    Task<PodcastEpisodePage> EpisodesAsync(PodcastEpisodeQuery query, CancellationToken token);
    Task<PodcastEpisode?> EpisodeAsync(string id, CancellationToken token);
    Task MarkPlayedAsync(string id, bool played, CancellationToken token);
    Task SaveProgressAsync(string id, double position, bool ended, CancellationToken token);
    void Cancel();
}
