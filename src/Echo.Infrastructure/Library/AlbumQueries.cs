using Echo.Contracts.Library;
using Echo.Infrastructure.Search;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    // Only the interactive SQLite owner touches this cache; counts never block every deep page.
    private readonly Dictionary<(string? Search, bool Lossless, string? Artist, ArtistGrouping Grouping, LibraryFilter? Filter), int> _albumCounts = [];
    private long _albumCountRevision = -1;
    public Task<AlbumPage> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellation = default)
        => QueryAlbumsAsync(query, null, ArtistGrouping.Credits, cancellation);

    private Task<AlbumPage> QueryAlbumsAsync(AlbumQuery query, string? artistId, ArtistGrouping grouping, CancellationToken cancellation)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > 200 || query.Search?.Length > 128 || !Enum.IsDefined(query.Sort))
            throw new ArgumentException("Invalid bounded album query.");
        LibraryFilterSql.Validate(query.Filter);
        return _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM library_state WHERE id=1";
            var revision = (long)command.ExecuteScalar()!;
            if (query.Revision is { } expected && expected != revision) throw new LibraryChangedException();
            var search = query.Search;
            var from = " FROM albums a JOIN album_identity i ON i.group_key=a.id WHERE 1=1 "
                + AlbumSearchSql.Build(command, search, query.Filter)
                + (query.LosslessOnly ? " AND a.lossless_count=a.track_count " : "")
                + (artistId is null ? "" : " AND a.id IN(SELECT album_id FROM artist_albums WHERE grouping=$group AND artist_id=$artist) ");
            if (artistId is not null) { command.Parameters.AddWithValue("$artist", artistId); command.Parameters.AddWithValue("$group", (int)grouping); }
            if (_albumCountRevision != revision) { _albumCounts.Clear(); _albumCountRevision = revision; }
            var countKey = (search, query.LosslessOnly, artistId, grouping, query.Filter);
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
                AlbumSort.Artist => "i.artist_key", AlbumSort.Year => "a.year", AlbumSort.Recent => "a.added",
                AlbumSort.Duration => "a.duration", AlbumSort.TrackCount => "a.track_count", _ => "i.title_key"
            };
            var direction = query.Descending ? " DESC" : " ASC";
            command.CommandText = "SELECT i.stable_id,i.display_title,i.display_artist,a.track_count,a.duration,a.year," +
                "(SELECT path FROM tracks WHERE album_key=a.id AND missing=0 ORDER BY disc_no,track_no,sort_key,id LIMIT 1)," +
                "a.lossless_count,a.added,coalesce((SELECT favorite FROM album_user_state WHERE album_id=a.id),0)," +
                "coalesce((SELECT rating FROM album_user_state WHERE album_id=a.id),0)" + from +
                $" ORDER BY {sort}{direction},i.stable_id{direction} LIMIT $limit OFFSET $offset";
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
        if (query.AlbumId.Length > 1100 || query.Offset < 0 || query.Limit is < 1 or > 200 || !Enum.IsDefined(query.Sort))
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
                SELECT id,path,title,artist,coalesce((SELECT display_title FROM album_identity WHERE group_key=tracks.album_key),album),duration,codec,sample_rate,bit_depth,sort_key,track_no,disc_no,favorite,source_json
                FROM tracks WHERE album_key=(SELECT group_key FROM album_identity WHERE stable_id=$album OR group_key=$album LIMIT 1) AND missing=0
                """ + DetailOrder(query.Sort, query.Descending, "") + " LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$album", query.AlbumId);
            command.Parameters.AddWithValue("$limit", query.Limit);
            command.Parameters.AddWithValue("$offset", query.Offset);
            var items = new List<TrackRow>(query.Limit);
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    items.Add(TrackRowMapping.Read(reader));
                }
            transaction.Commit();
            return new TrackPage(items, null, revision);
        }, true, cancellation);
    }

    private static string DetailOrder(DetailTrackSort sort, bool descending, string prefix)
    {
        var direction = descending ? " DESC" : " ASC";
        string[] fields = sort switch
        {
            DetailTrackSort.Title => ["sort_key"], DetailTrackSort.Duration => ["duration"],
            DetailTrackSort.Quality => ["sample_rate", "bit_depth", "sort_key"],
            _ => ["disc_no", "track_no", "sort_key"]
        };
        return " ORDER BY " + string.Join(",", fields.Append("id").Select(field => prefix + field + direction));
    }
}
