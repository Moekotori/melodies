namespace Echo.Contracts.Library;

public enum GenreSort { Name, TrackCount, AlbumCount }
public sealed record GenreRow(string Id, string Name, int TrackCount, int AlbumCount,
    double DurationSeconds, string SamplePath);
public sealed record GenreQuery(int Offset = 0, int Limit = 200, string? Search = null,
    GenreSort Sort = GenreSort.TrackCount, bool Descending = true, long? Revision = null);
public sealed record GenrePage(IReadOnlyList<GenreRow> Items, int Total, long Revision);
public sealed record GenreTrackQuery(string GenreId, int Offset = 0, int Limit = 200,
    long? Revision = null, DetailTrackSort Sort = DetailTrackSort.Order, bool Descending = false);

public interface IGenreLibrary
{
    Task<GenrePage> GetGenresAsync(GenreQuery query, CancellationToken cancellation = default);
    Task<TrackPage> GetGenreTracksAsync(GenreTrackQuery query, CancellationToken cancellation = default);
    Task<AlbumPage> GetGenreAlbumsAsync(string genreId, AlbumQuery query, CancellationToken cancellation = default);
}
