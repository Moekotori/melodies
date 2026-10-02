using Echo.Contracts.Library;

namespace Echo.Desktop.Bootstrap;

internal sealed partial class DesktopLibrary : IPlaylistLibrary
{
    public async Task<PlaylistPage> GetPlaylistsAsync(PlaylistQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetPlaylistsAsync(query, cancellation).ConfigureAwait(false);
    public async Task<PlaylistTrackPage> GetPlaylistTracksAsync(PlaylistTrackQuery query, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetPlaylistTracksAsync(query, cancellation).ConfigureAwait(false);
    public async Task<string> CreatePlaylistAsync(string operationId, string name, string description, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).CreatePlaylistAsync(operationId, name, description, cancellation).ConfigureAwait(false);
    public async Task UpdatePlaylistAsync(string operationId, string id, string name, string description, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).UpdatePlaylistAsync(operationId, id, name, description, cancellation).ConfigureAwait(false);
    public async Task ArchivePlaylistAsync(string operationId, string id, bool archived, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).ArchivePlaylistAsync(operationId, id, archived, cancellation).ConfigureAwait(false);
    public async Task AddPlaylistTracksAsync(string operationId, string id, IReadOnlyList<string> trackIds, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).AddPlaylistTracksAsync(operationId, id, trackIds, cancellation).ConfigureAwait(false);
    public async Task RemovePlaylistItemsAsync(string operationId, string id, IReadOnlyList<string> itemIds, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).RemovePlaylistItemsAsync(operationId, id, itemIds, cancellation).ConfigureAwait(false);
    public async Task MovePlaylistItemAsync(string operationId, string id, string itemId, bool up, long revision, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).MovePlaylistItemAsync(operationId, id, itemId, up, revision, cancellation).ConfigureAwait(false);
}
