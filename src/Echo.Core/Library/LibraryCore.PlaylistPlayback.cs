using Echo.Contracts.Library;
using Echo.Core.Playlists;

namespace Echo.Core.Library;

public sealed partial class LibraryCore
{
    public async Task PlayPlaylistAsync(PlaylistTrackQuery query, bool append = false, bool next = false, bool shuffle = false, CancellationToken cancellation = default)
    {
        var name = query.PlaylistId is { } id
            ? (await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetPlaylistMetadataAsync(id, cancellation).ConfigureAwait(false)).Name : "喜欢的歌曲";
        await new PlaylistPlaybackService(this, playback ?? throw new InvalidOperationException("Core 播放服务未连接。"), name)
            .PlayAsync(query, append, next, shuffle, cancellation).ConfigureAwait(false);
    }
}
