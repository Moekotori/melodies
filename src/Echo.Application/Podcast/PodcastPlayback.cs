using Echo.Contracts.Podcast;
using Echo.Playback;

namespace Echo.Application.Podcast;

public sealed class PodcastPlayback(IPlaybackSession session)
{
    public static QueueTrack Track(PodcastEpisode episode, PodcastFeed feed)
    {
        var metadata = new Dictionary<string, string?>
        {
            ["title"] = episode.Title, ["artist"] = feed.Author, ["album"] = feed.Title, ["podcastFeedId"] = feed.Id
        };
        if (episode.Duration is > 0) metadata["duration"] = episode.Duration.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new(episode.Id, episode.Id, episode.AudioUrl, new("http", episode.AudioUrl), metadata, Origin: new("podcast", feed.Title, feed.Id));
    }

    public async Task StartAsync(PodcastEpisode episode, PodcastFeed feed, bool fromBeginning, bool toggle, CancellationToken token)
    {
        var snapshot = session.Snapshot;
        if (!fromBeginning && snapshot.Current?.ItemId == episode.Id && snapshot.Status is PlaybackStatus.Playing or PlaybackStatus.Paused)
        {
            if (snapshot.Status == PlaybackStatus.Paused) await session.ResumeAsync(token).ConfigureAwait(false);
            else if (toggle) await session.PauseAsync(token).ConfigureAwait(false);
            return;
        }
        var start = fromBeginning || episode.Played ? 0 : episode.Position;
        await session.PlayAsync(Track(episode, feed), token).ConfigureAwait(false);
        var opened = session.Snapshot;
        if (start > 0 && opened.Current?.ItemId == episode.Id && opened.Status is not PlaybackStatus.Faulted)
            await session.SeekAsync(start, token).ConfigureAwait(false);
    }

    public Task EnqueueNextAsync(PodcastEpisode episode, PodcastFeed feed, CancellationToken token)
    {
        var track = Track(episode, feed);
        return session.QueueState.Items.Any(item => item.ItemId == track.ItemId)
            ? session.MoveQueueItemsNextAsync([track.ItemId], token)
            : session.EnqueueAsync([track], nextUp: true, token);
    }
}

public sealed class PodcastProgressWriter : IAsyncDisposable
{
    private readonly IPlaybackSession session;
    private readonly PodcastProgressTracker tracker;
    private string? seenId;
    private PlaybackStatus seenStatus;
    private double seenPosition = double.NaN;

    public PodcastProgressWriter(IPlaybackSession session, IPodcastLibrary library)
    {
        this.session = session;
        tracker = new(sample => library.SaveProgressAsync(sample.Id, sample.Position, sample.Ended, CancellationToken.None));
        session.Changed += OnChanged;
    }

    public async ValueTask DisposeAsync()
    {
        session.Changed -= OnChanged;
        await tracker.DisposeAsync().ConfigureAwait(false);
    }

    private void OnChanged(PlaybackSnapshot snapshot)
    {
        var id = snapshot.Current?.ItemId;
        if (id == seenId && snapshot.Status == seenStatus && snapshot.PositionSeconds == seenPosition) return;
        seenId = id; seenStatus = snapshot.Status; seenPosition = snapshot.PositionSeconds;
        tracker.Observe(id, snapshot.Status switch
        {
            PlaybackStatus.Playing => "playing", PlaybackStatus.Paused => "paused", PlaybackStatus.Ended => "ended", _ => ""
        }, snapshot.PositionSeconds);
    }
}
