namespace Echo.Contracts.Library;

public enum ArtistGrouping { Credits, AlbumArtist }
public enum ArtistSort { Name, TrackCount, AlbumCount, Recent }
public sealed record ArtistRow(string Id, string Name, int TrackCount, int AlbumCount,
    double DurationSeconds, string SamplePath, ArtistGrouping Grouping, string? PortraitPath = null);
public sealed record ArtistQuery(int Offset = 0, int Limit = 200, string? Search = null,
    ArtistSort Sort = ArtistSort.Name, bool Descending = false, ArtistGrouping Grouping = ArtistGrouping.Credits,
    long? Revision = null, LibraryFilter? Filter = null);
public sealed record ArtistPage(IReadOnlyList<ArtistRow> Items, int Total, long Revision);
public sealed record ArtistTrackQuery(string ArtistId, ArtistGrouping Grouping, int Offset = 0, int Limit = 200, long? Revision = null,
    DetailTrackSort Sort = DetailTrackSort.Order, bool Descending = false);

public interface IArtistLibrary
{
    Task<ArtistInsights> GetArtistInsightsAsync(string id, ArtistGrouping grouping, CancellationToken cancellation = default);
    Task<ArtistPage> GetArtistsAsync(ArtistQuery query, CancellationToken cancellation = default);
    Task<TrackPage> GetArtistTracksAsync(ArtistTrackQuery query, CancellationToken cancellation = default);
    Task<AlbumPage> GetArtistAlbumsAsync(string artistId, ArtistGrouping grouping, AlbumQuery query, CancellationToken cancellation = default);
}
public interface IArtistArtwork
{
    Task<byte[]?> ReadArtistAsync(string samplePath, CancellationToken cancellation);
}
