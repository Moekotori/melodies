namespace Echo.Contracts.Library;

// Keep existing values stable for saved view preferences.
public enum AlbumSort { Title, Artist, Year, Recent, Duration, TrackCount, Default, LastPlayed, PlayCount, Random, Created, Modified }
public sealed record AlbumRow(string Id, string Title, string Artist, int TrackCount,
    double DurationSeconds, int? Year, string SamplePath, int LosslessCount, long AddedUnix = 0,
    bool Favorite = false, int Rating = 0);
public sealed record AlbumQuery(int Offset = 0, int Limit = 200, string? Search = null,
    AlbumSort Sort = AlbumSort.Title, bool Descending = false, bool LosslessOnly = false,
    long? Revision = null, LibraryFilter? Filter = null, string? ShufflePivot = null);
public sealed record AlbumPage(IReadOnlyList<AlbumRow> Items, int Total, long Revision);
public enum DetailTrackSort { Order, Title, Duration, Quality }
public sealed record AlbumTrackQuery(string AlbumId, int Offset = 0, int Limit = 200, long? Revision = null,
    DetailTrackSort Sort = DetailTrackSort.Order, bool Descending = false);

// This is a read boundary, separate from the scan/write owner.
public interface IAlbumLibrary
{
    Task<AlbumPage> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellation = default);
    Task<TrackPage> GetAlbumTracksAsync(AlbumTrackQuery query, CancellationToken cancellation = default);
}

public interface IAlbumArtwork
{
    // Small encoded local artwork; decoding and image ownership belong to the view resource service.
    Task<byte[]?> ReadAsync(string samplePath, CancellationToken cancellation);
}

public interface IAlbumPersonalState
{
    Task SetAlbumStateAsync(string albumId, bool favorite, int rating, CancellationToken cancellation);
    Task SetTrackFavoriteAsync(string trackId, bool favorite, CancellationToken cancellation);
}
