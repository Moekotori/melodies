using System.Globalization;
using Echo.Contracts.Library;
using Echo.Playback;

namespace Echo.Core.Playlists;

// Only Core prepares the complete bounded selection and submits it to the existing session owner.
internal sealed class PlaylistPlaybackService(IPlaylistLibrary library, IPlaybackSession session, string name)
{
    public async Task PlayAsync(PlaylistTrackQuery query, bool append, bool next, bool shuffle, CancellationToken cancellation)
    {
        var tracks = new List<QueueTrack>(); long? revision = query.Revision; var offset = 0;
        var origin = new QueueOrigin(query.PlaylistId is null ? "liked" : "playlist", name, query.PlaylistId);
        while (true)
        {
            var page = await library.GetPlaylistTracksAsync(query with { Offset = offset, Limit = 200, Revision = revision }, cancellation).ConfigureAwait(false);
            revision = page.Revision;
            if (page.Total > PlaybackSessionCoordinator.QueueCapacity)
                throw new ArgumentException("完整播放最多 1000 项；更长歌单请使用播放本页。");
            foreach (var entry in page.Items)
            {
                var row = entry.Track;
                var remote = row.Source?.Kind == "remote" || row.Path.StartsWith("remote://", StringComparison.Ordinal);
                var source = remote ? new AudioSource("remote", row.Path) : row.Source is { } facts
                    ? AudioSource.Local(facts.AudioPath, facts.Kind == "cue" ? facts.StartSeconds : 0, facts.Kind == "cue" ? facts.EndSeconds : null) : AudioSource.Local(row.Path);
                tracks.Add(new(Guid.NewGuid().ToString("N"), row.Id, remote ? row.Path : source.Uri, source,
                    new Dictionary<string, string?> { ["title"] = row.Title, ["artist"] = row.Artist, ["album"] = row.Album,
                        ["duration"] = row.DurationSeconds.ToString(CultureInfo.InvariantCulture), ["codec"] = row.Codec },
                    Origin: origin, SampleRate: row.SampleRate > 0 ? row.SampleRate : null));
            }
            offset += page.Items.Count;
            if (offset >= page.Total) break;
            if (page.Items.Count == 0) throw new LibraryChangedException();
        }
        if (tracks.Count == 0) throw new InvalidOperationException("歌单没有可播放的曲目。");
        if (append && tracks.Count + session.Queue.Count > PlaybackSessionCoordinator.QueueCapacity)
            throw new ArgumentException("加入后将超过 1000 项播放窗口，请先整理队列。");
        var items = tracks.ToArray();
        if (shuffle) Random.Shared.Shuffle(items);
        if (append) await session.EnqueueAsync(items, next, cancellation).ConfigureAwait(false);
        else await session.SetQueueAsync(items, items[0].ItemId, cancellation).ConfigureAwait(false);
    }
}
