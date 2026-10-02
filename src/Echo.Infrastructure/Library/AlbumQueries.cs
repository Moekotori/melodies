using Echo.Contracts.Library;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    // Only the interactive SQLite owner touches this cache; counts never block every deep page.
    private readonly Dictionary<(string? Search, bool Lossless, string? Artist, ArtistGrouping Grouping), int> _albumCounts = [];
    private long _albumCountRevision = -1;
    public Task<AlbumPage> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellation = default)
        => QueryAlbumsAsync(query, null, ArtistGrouping.Credits, cancellation);

    private Task<AlbumPage> QueryAlbumsAsync(AlbumQuery query, string? artistId, ArtistGrouping grouping, CancellationToken cancellation)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > 200 || query.Search?.Length > 128 || !Enum.IsDefined(query.Sort))
            throw new ArgumentException("Invalid bounded album query.");
        return _interactiveReader.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM library_state WHERE id=1";
            var revision = (long)command.ExecuteScalar()!;
            if (query.Revision is { } expected && expected != revision) throw new LibraryChangedException();
            var search = FtsQuery(query.Search);
            var from = " FROM albums a " + (search is null ? "" : " JOIN albums_fts ON albums_fts.rowid=a.rowid ") +
                " WHERE 1=1 " + (search is null ? "" : " AND albums_fts MATCH $search ") +
                (query.LosslessOnly ? " AND a.lossless_count=a.track_count " : "") +
                (artistId is null ? "" : " AND a.id IN(SELECT album_id FROM artist_albums WHERE grouping=$group AND artist_id=$artist) ");
            if (artistId is not null) { command.Parameters.AddWithValue("$artist", artistId); command.Parameters.AddWithValue("$group", (int)grouping); }
            if (search is not null) command.Parameters.AddWithValue("$search", search);
            if (_albumCountRevision != revision) { _albumCounts.Clear(); _albumCountRevision = revision; }
            var countKey = (search, query.LosslessOnly, artistId, grouping);
            if (!_albumCounts.TryGetValue(countKey, out var total))
            {
                command.CommandText = "SELECT count(*)" + from;
                total = checked((int)(long)command.ExecuteScalar()!);
                if (_albumCounts.Count >= 16) _albumCounts.Remove(_albumCounts.Keys.First());
                _albumCounts[countKey] = total;
            }
            cancellation.ThrowIfCancellationRequested();
            var sort = query.Sort switch
            {
                AlbumSort.Artist => "artist_key", AlbumSort.Year => "year", AlbumSort.Recent => "added",
                AlbumSort.Duration => "duration", AlbumSort.TrackCount => "track_count", _ => "sort_key"
            };
            var direction = query.Descending ? " DESC" : " ASC";
            command.CommandText = "SELECT a.id,a.title,a.artist,a.track_count,a.duration,a.year," +
                "(SELECT path FROM tracks WHERE album_key=a.id AND missing=0 ORDER BY disc_no,track_no,sort_key,id LIMIT 1)," +
                "a.lossless_count,a.added,coalesce((SELECT favorite FROM album_user_state WHERE album_id=a.id),0)," +
                "coalesce((SELECT rating FROM album_user_state WHERE album_id=a.id),0)" + from +
                $" ORDER BY a.{sort}{direction},a.id{direction} LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$limit", query.Limit);
            command.Parameters.AddWithValue("$offset", query.Offset);
            var items = new List<AlbumRow>(query.Limit);
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                        reader.GetDouble(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetString(6), reader.GetInt32(7),
                        reader.GetInt64(8), reader.GetBoolean(9), reader.GetInt32(10)));
                }
            transaction.Commit();
            return new AlbumPage(items, total, revision);
        }, true, cancellation);
    }

    public Task<TrackPage> GetAlbumTracksAsync(AlbumTrackQuery query, CancellationToken cancellation = default)
    {
        if (query.AlbumId.Length > 1100 || query.Offset < 0 || query.Limit is < 1 or > 200)
            throw new ArgumentException("Invalid bounded album track query.");
        return _interactiveReader.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM library_state WHERE id=1";
            var revision = (long)command.ExecuteScalar()!;
            if (query.Revision is { } expected && expected != revision) throw new LibraryChangedException();
            command.CommandText = """
                SELECT id,path,title,artist,album,duration,codec,sample_rate,bit_depth,sort_key,track_no,disc_no,favorite
                FROM tracks WHERE album_key=$album AND missing=0
                ORDER BY disc_no,track_no,sort_key,id LIMIT $limit OFFSET $offset
                """;
            command.Parameters.AddWithValue("$album", query.AlbumId);
            command.Parameters.AddWithValue("$limit", query.Limit);
            command.Parameters.AddWithValue("$offset", query.Offset);
            var items = new List<TrackRow>(query.Limit);
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                        reader.GetString(4), reader.GetDouble(5), reader.GetString(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetString(9),
                        reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetInt32(11), reader.GetBoolean(12)));
                }
            transaction.Commit();
            return new TrackPage(items, null, revision);
        }, true, cancellation);
    }
}
