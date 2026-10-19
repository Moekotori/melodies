using System.Text.Json;
using Echo.Core.Contracts;
using Echo.Playback;

namespace Echo.Core.Hosting;

internal sealed partial class CoreDispatch
{
    // An explicit capability list: new RPCs never become remotely reachable by naming convention.
    private static bool RemoteControlAllowed(string method) => method is
        "core.info" or "core.detach" or "settings.features.read" or "background.wait"
        or "library.GetHomeAsync" or "library.GetStatsAsync" or "library.GetTracksAsync" or "library.GetAlbumsAsync"
        or "library.GetAlbumTracksAsync"
        or "library.GetArtistsAsync" or "library.GetArtistAlbumsAsync" or "library.GetArtistTracksAsync"
        or "library.GetGenresAsync" or "library.GetGenreTracksAsync" or "library.GetGenreAlbumsAsync"
        or "library.GetFoldersAsync" or "library.GetFolderTracksAsync" or "library.GetRandomTracksAsync"
        or "library.FindTrackByPathAsync" or "library.GetTrackAlbumAsync"
        or "library.GetTrackTagsAsync" or "library.GetAlbumMetadataAsync" or "library.GetHistoryEpochAsync"
        or "library.GetPlaylistsAsync" or "library.GetPlaylistTracksAsync" or "library.GetHistoryAsync"
        or "library.SetTrackFavoriteAsync" or "library.SetAlbumStateAsync"
        or "playback.PauseAsync" or "playback.ResumeAsync" or "playback.StopAsync" or "playback.SeekAsync"
        or "playback.NextAsync" or "playback.PreviousAsync" or "playback.SetVolumeAsync"
        or "playback.RemoveQueueItemsAsync" or "playback.ClearQueueAsync" or "playback.MoveQueueItemsAsync"
        or "playback.MoveQueueItemsNextAsync" or "playback.SetRemoveAfterPlayAsync" or "playback.UndoQueueEditAsync"
        or "playback.SelectQueueItemAsync" or "playback.SetQueueModeAsync" or "playback.SetRepeatAsync";

    private async Task<JsonElement?> DispatchRemoteControlAsync(string method, JsonElement payload, CancellationToken token)
    {
        var session = runtime.Playback.LocalZone.Session;
        switch (method)
        {
            case "playback.ResolveFilesAsync":
                var request = CoreJson.Read<PlaybackResolveFilesRequest>(payload);
                if (request.Paths.Count > 1000) throw new ArgumentException("一次最多选择 1000 项。");
                var resolved = new List<QueueTrack>(request.Paths.Count);
                foreach (var path in request.Paths) resolved.Add(await IndexedTrackAsync(path, Guid.NewGuid().ToString("N"), token).ConfigureAwait(false));
                return CoreJson.Value<IReadOnlyList<QueueTrack>>(resolved);
            case "playback.PlayAsync":
                var track = CoreJson.Read<PlaybackPlayRequest>(payload).Track;
                await session.PlayAsync(await IndexedTrackAsync(track.FilePath, track.ItemId, token).ConfigureAwait(false), token).ConfigureAwait(false);
                Changed?.Invoke(); return CoreJson.Empty;
            case "playback.SetQueueAsync":
                var queue = CoreJson.Read<PlaybackSetQueueRequest>(payload);
                await session.SetQueueAsync(await IndexedTracksAsync(queue.Tracks, token).ConfigureAwait(false), queue.StartItemId, token).ConfigureAwait(false);
                Changed?.Invoke(); return CoreJson.Empty;
            case "playback.EnqueueAsync":
                var enqueue = CoreJson.Read<PlaybackEnqueueRequest>(payload);
                await session.EnqueueAsync(await IndexedTracksAsync(enqueue.Tracks, token).ConfigureAwait(false), enqueue.NextUp, token).ConfigureAwait(false);
                Changed?.Invoke(); return CoreJson.Empty;
            case "resource.read":
                var resource = CoreJson.Read<CoreReadResource>(payload);
                if (resource.ExplicitPath is not null || resource.Kind is not ("artwork" or "lyrics" or "description")) throw new UnauthorizedAccessException();
                _ = await IndexedTrackAsync(resource.Path, "resource", token).ConfigureAwait(false);
                return await ReadResourceAsync(resource, token).ConfigureAwait(false);
        }
        if (!RemoteControlAllowed(method)) throw new UnauthorizedAccessException();
        return null;
    }
    private async Task<IReadOnlyList<QueueTrack>> IndexedTracksAsync(IReadOnlyList<QueueTrack> requested, CancellationToken token)
    {
        if (requested.Count > 1000) throw new ArgumentException("队列最多 1000 项。");
        var result = new List<QueueTrack>(requested.Count);
        foreach (var track in requested) result.Add(await IndexedTrackAsync(track.FilePath, track.ItemId, token).ConfigureAwait(false));
        return result;
    }
    private async Task<QueueTrack> IndexedTrackAsync(string path, string itemId, CancellationToken token)
    {
        if (string.IsNullOrEmpty(itemId) || itemId.Length > 128 || string.IsNullOrEmpty(path) || path.Length > 4096) throw new ArgumentException("无效的曲目标识。");
        var row = await runtime.Library.FindTrackByPathAsync(path, token).ConfigureAwait(false) ?? throw new UnauthorizedAccessException();
        // Reconstruct from Core-owned facts. Never accept a remote URI, headers, CUE range or metadata override.
        var source = row.Path.StartsWith("remote://", StringComparison.Ordinal) ? new AudioSource("remote", row.Path)
            : row.Source is { } facts ? AudioSource.Local(facts.AudioPath, facts.StartSeconds, facts.EndSeconds) : AudioSource.Local(row.Path);
        var metadata = new Dictionary<string, string?> { ["title"] = row.Title, ["artist"] = row.Artist, ["album"] = row.Album };
        return new(itemId, row.Id, row.Path, source, metadata, SampleRate: row.SampleRate);
    }
}
