using Echo.Contracts.Library;
using Echo.Infrastructure.Search;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : IGenreLibrary
{
    private readonly Dictionary<string, int> _genreCounts = [];
    private long _genreCountRevision = -1;
    public Task<GenrePage> GetGenresAsync(GenreQuery query, CancellationToken cancellation = default)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > 200 || query.Search?.Length > 128 || !Enum.IsDefined(query.Sort))
            throw new ArgumentException("Invalid bounded genre page.");
        return _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM library_state WHERE id=1";
            var revision = (long)command.ExecuteScalar()!;
            if (query.Revision is { } expected && revision != expected) throw new LibraryChangedException();
            var search = query.Search?.Trim() ?? "";
            var from = " FROM genres g WHERE 1=1";
            if (search.Length > 0)
            {
                from += " AND echo_fold(CASE WHEN g.id='__unclassified__' THEN '未分类 Unclassified' ELSE g.name END) LIKE $search ESCAPE '\\'";
                command.Parameters.AddWithValue("$search", "%" + LibrarySearchSql.Escape(LibrarySearchSchema.Fold(search)) + "%");
            }
            if (_genreCountRevision != revision) { _genreCounts.Clear(); _genreCountRevision = revision; }
            if (!_genreCounts.TryGetValue(search, out var total))
            {
                command.CommandText = "SELECT count(*)" + from; total = checked((int)(long)command.ExecuteScalar()!);
                if (_genreCounts.Count >= 16) _genreCounts.Remove(_genreCounts.Keys.First());
                _genreCounts[search] = total;
            }
            var field = query.Sort switch { GenreSort.TrackCount => "track_count", GenreSort.AlbumCount => "album_count", _ => "id" };
            var direction = query.Descending ? " DESC" : " ASC";
            command.CommandText = "SELECT g.id,(SELECT name FROM genre_tracks WHERE genre_id=g.id GROUP BY name ORDER BY count(*) DESC,name LIMIT 1),g.track_count,g.album_count,g.duration," +
                "(SELECT t.path FROM genre_tracks m JOIN tracks t ON t.id=m.track_id WHERE m.genre_id=g.id ORDER BY m.track_id LIMIT 1)" +
                from + $" ORDER BY g.{field}{direction},g.id{direction} LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$limit", query.Limit); command.Parameters.AddWithValue("$offset", query.Offset);
            var rows = new List<GenreRow>(query.Limit);
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetDouble(4), reader.GetString(5)));
                }
            transaction.Commit(); return new GenrePage(rows, total, revision);
        }, true, cancellation);
    }
    public Task<TrackPage> GetGenreTracksAsync(GenreTrackQuery query, CancellationToken cancellation = default)
    {
        ValidateGenre(query.GenreId);
        if (query.Offset < 0 || query.Limit is < 1 or > 200 || !Enum.IsDefined(query.Sort)) throw new ArgumentException("Invalid bounded genre tracks.");
        return _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM library_state WHERE id=1";
            var revision = (long)command.ExecuteScalar()!;
            if (query.Revision is { } expected && revision != expected) throw new LibraryChangedException();
            command.CommandText = """
                SELECT t.id,t.path,t.title,t.artist,t.album,t.duration,t.codec,t.sample_rate,t.bit_depth,t.sort_key,t.track_no,t.disc_no,t.favorite,t.source_json
                FROM genre_tracks m JOIN tracks t ON t.id=m.track_id WHERE m.genre_id=$genre AND t.missing=0
                """ + DetailOrder(query.Sort, query.Descending, "t.") + " LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$genre", query.GenreId); command.Parameters.AddWithValue("$limit", query.Limit);
            command.Parameters.AddWithValue("$offset", query.Offset);
            var rows = new List<TrackRow>(query.Limit);
            using (var reader = command.ExecuteReader())
                while (reader.Read()) { cancellation.ThrowIfCancellationRequested(); rows.Add(TrackRowMapping.Read(reader)); }
            transaction.Commit(); return new TrackPage(rows, null, revision);
        }, true, cancellation);
    }
    public Task<AlbumPage> GetGenreAlbumsAsync(string genreId, AlbumQuery query, CancellationToken cancellation = default)
    { ValidateGenre(genreId); return QueryAlbumsAsync(query, null, ArtistGrouping.Credits, cancellation, genreId); }
    private static void ValidateGenre(string id)
    { if (id.Length is 0 or > 4096) throw new ArgumentException("Invalid genre reference."); }
}
