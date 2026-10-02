namespace Echo.Contracts.Library;

public sealed record PlaylistRow(string Id, string Name, string Description, long TrackCount,
    double DurationSeconds, string? SamplePath, long UpdatedUnix);
public sealed record PlaylistQuery(int Offset = 0, int Limit = 200, string? Search = null, long? Revision = null);
public sealed record PlaylistPage(IReadOnlyList<PlaylistRow> Items, long Total, long LikedTracks, long LikedAlbums, long Revision);
public enum PlaylistTrackSort { Manual, Title, Artist, Album, Duration, Quality }
public sealed record PlaylistTrackQuery(string? PlaylistId, int Offset = 0, int Limit = 200,
    string? Search = null, PlaylistTrackSort Sort = PlaylistTrackSort.Manual, bool Descending = false, long? Revision = null);
public sealed record PlaylistTrackRow(string ItemId, long Position, TrackRow Track);
public sealed record PlaylistTrackPage(IReadOnlyList<PlaylistTrackRow> Items, long Total, double DurationSeconds, long Revision);

// Null playlist selects liked tracks. Commands share the existing library write owner.
public interface IPlaylistLibrary
{
    Task<PlaylistPage> GetPlaylistsAsync(PlaylistQuery query, CancellationToken cancellation = default);
    Task<PlaylistTrackPage> GetPlaylistTracksAsync(PlaylistTrackQuery query, CancellationToken cancellation = default);
    Task<string> CreatePlaylistAsync(string operationId, string name, string description, CancellationToken cancellation = default);
    Task UpdatePlaylistAsync(string operationId, string id, string name, string description, CancellationToken cancellation = default);
    Task ArchivePlaylistAsync(string operationId, string id, bool archived, CancellationToken cancellation = default);
    Task AddPlaylistTracksAsync(string operationId, string id, IReadOnlyList<string> trackIds, CancellationToken cancellation = default);
    Task RemovePlaylistItemsAsync(string operationId, string id, IReadOnlyList<string> itemIds, CancellationToken cancellation = default);
    Task MovePlaylistItemAsync(string operationId, string id, string itemId, bool up, long revision, CancellationToken cancellation = default);
}
