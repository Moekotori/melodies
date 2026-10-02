using Echo.Contracts.Library;
using Echo.Infrastructure.Search;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    private readonly Dictionary<(string? Search, ArtistGrouping Grouping, LibraryFilter? Filter), int> _artistCounts = [];
    private long _artistCountRevision = -1;
    public Task<ArtistPage> GetArtistsAsync(ArtistQuery query, CancellationToken cancellation = default)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > 200 || query.Search?.Length > 128 || !Enum.IsDefined(query.Sort) || !Enum.IsDefined(query.Grouping))
            throw new ArgumentException("Invalid bounded artist page.");
        LibraryFilterSql.Validate(query.Filter);
        return _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM library_state WHERE id=1";
            var revision = (long)command.ExecuteScalar()!;
            if (query.Revision is { } expected && revision != expected) throw new LibraryChangedException();
            var search = query.Search;
            var from = " FROM artists a WHERE a.grouping=$group " + LibrarySearchSql.Entity(command, search, "a.name");
            if (LibraryFilterSql.Active(query.Filter))
                from += " AND EXISTS(SELECT 1 FROM artist_tracks m JOIN tracks t ON t.id=m.track_id WHERE m.grouping=a.grouping AND m.artist_id=a.id AND t.missing=0" + LibraryFilterSql.Build(command, query.Filter) + ")";
            command.Parameters.AddWithValue("$group", (int)query.Grouping);
            if (_artistCountRevision != revision) { _artistCounts.Clear(); _artistCountRevision = revision; }
            var key = (search, query.Grouping, query.Filter);
            if (!_artistCounts.TryGetValue(key, out var total))
            {
                command.CommandText = "SELECT count(*)" + from;
                total = checked((int)(long)command.ExecuteScalar()!);
                if (_artistCounts.Count >= 16) _artistCounts.Remove(_artistCounts.Keys.First());
                _artistCounts[key] = total;
            }
            cancellation.ThrowIfCancellationRequested();
            var field = query.Sort switch { ArtistSort.TrackCount => "track_count", ArtistSort.AlbumCount => "album_count", ArtistSort.Recent => "added", _ => "sort_key" };
            var direction = query.Descending ? " DESC" : " ASC";
            command.CommandText = "SELECT a.id,a.name,a.track_count,a.album_count,a.duration," +
                "(SELECT t.path FROM artist_tracks m JOIN tracks t ON t.id=m.track_id WHERE m.grouping=a.grouping AND m.artist_id=a.id ORDER BY m.track_id LIMIT 1)," +
                "(SELECT t.path FROM artist_tracks m JOIN tracks t ON t.id=m.track_id WHERE m.grouping=a.grouping AND m.artist_id=a.id AND t.album_credit=a.name COLLATE NOCASE ORDER BY m.track_id LIMIT 1)" +
                from + $" ORDER BY a.{field}{direction},a.id{direction} LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$limit", query.Limit); command.Parameters.AddWithValue("$offset", query.Offset);
            var items = new List<ArtistRow>(query.Limit);
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetDouble(4), reader.GetString(5), query.Grouping,
                        reader.IsDBNull(6) ? null : reader.GetString(6)));
                }
            transaction.Commit(); return new ArtistPage(items, total, revision);
        }, true, cancellation);
    }

    public Task<TrackPage> GetArtistTracksAsync(ArtistTrackQuery query, CancellationToken cancellation = default)
    {
        ValidateArtist(query.ArtistId, query.Grouping);
        if (query.Offset < 0 || query.Limit is < 1 or > 200 || !Enum.IsDefined(query.Sort)) throw new ArgumentException("Invalid bounded artist tracks.");
        return _interactiveReader.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM library_state WHERE id=1";
            var revision = (long)command.ExecuteScalar()!;
            if (query.Revision is { } expected && revision != expected) throw new LibraryChangedException();
            command.CommandText = """
                SELECT t.id,t.path,t.title,t.artist,t.album,t.duration,t.codec,t.sample_rate,t.bit_depth,t.sort_key,t.track_no,t.disc_no,t.favorite,t.source_json
                FROM artist_tracks m JOIN tracks t ON t.id=m.track_id WHERE m.grouping=$group AND m.artist_id=$artist AND t.missing=0
                """ + (query.Sort == DetailTrackSort.Order ? " ORDER BY m.track_id" + (query.Descending ? " DESC" : " ASC")
                    : DetailOrder(query.Sort, query.Descending, "t.")) + " LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$group", (int)query.Grouping); command.Parameters.AddWithValue("$artist", query.ArtistId);
            command.Parameters.AddWithValue("$limit", query.Limit); command.Parameters.AddWithValue("$offset", query.Offset);
            var items = new List<TrackRow>(query.Limit);
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    items.Add(TrackRowMapping.Read(reader));
                }
            transaction.Commit(); return new TrackPage(items, null, revision);
        }, true, cancellation);
    }

    public Task<AlbumPage> GetArtistAlbumsAsync(string artistId, ArtistGrouping grouping, AlbumQuery query, CancellationToken cancellation = default)
    { ValidateArtist(artistId, grouping); return QueryAlbumsAsync(query, artistId, grouping, cancellation); }
    private static void ValidateArtist(string id, ArtistGrouping grouping)
    { if (id.Length is 0 or > 1024 || !Enum.IsDefined(grouping)) throw new ArgumentException("Invalid artist reference."); }
}
