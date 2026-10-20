using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : IPlaylistLibrary
{
    public Task<PlaylistPage> GetPlaylistsAsync(PlaylistQuery query, CancellationToken cancellation = default)
    {
        ValidatePlaylistPage(query.Offset, query.Limit, query.Search);
        return _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            var revision = PlaylistRevision(command, query.Revision);
            var where = " FROM playlists p WHERE p.archived=$archived";
            command.Parameters.AddWithValue("$archived", query.Archived);
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                where += " AND instr(echo_fold(p.name),echo_fold($search))>0";
                command.Parameters.AddWithValue("$search", query.Search.Trim());
            }
            command.CommandText = "SELECT count(*)" + where; var total = (long)command.ExecuteScalar()!;
            command.CommandText = "SELECT count(*) FROM tracks WHERE favorite=1 AND missing=0";
            var liked = (long)command.ExecuteScalar()!;
            command.CommandText = "SELECT count(*) FROM album_user_state s JOIN albums a ON a.id=s.album_id WHERE s.favorite=1";
            var likedAlbums = (long)command.ExecuteScalar()!;
            command.CommandText = """
                SELECT p.id,p.name,p.description,
                  (SELECT count(*) FROM playlist_items pi JOIN tracks t ON t.id=pi.track_id WHERE pi.playlist_id=p.id AND t.missing=0),
                  coalesce((SELECT sum(t.duration) FROM playlist_items pi JOIN tracks t ON t.id=pi.track_id WHERE pi.playlist_id=p.id AND t.missing=0),0),
                  (SELECT t.path FROM playlist_items pi JOIN tracks t ON t.id=pi.track_id WHERE pi.playlist_id=p.id AND t.missing=0 ORDER BY pi.position,pi.id LIMIT 1),p.updated_at
                """ + where + " ORDER BY p.name COLLATE NOCASE,p.id LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$limit", query.Limit); command.Parameters.AddWithValue("$offset", query.Offset);
            var items = new List<PlaylistRow>(query.Limit);
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
                        reader.GetDouble(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6)));
                }
            transaction.Commit(); return new PlaylistPage(items, total, liked, likedAlbums, revision);
        }, true, cancellation);
    }

    public Task<PlaylistTrackPage> GetPlaylistTracksAsync(PlaylistTrackQuery query, CancellationToken cancellation = default)
    {
        ValidatePlaylistPage(query.Offset, query.Limit, query.Search);
        if (!Enum.IsDefined(query.Sort)) throw new ArgumentException("Invalid playlist sort.");
        if (query.PlaylistId is not null) ValidatePlaylistId(query.PlaylistId);
        return _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            var revision = PlaylistRevision(command, query.Revision);
            var liked = query.PlaylistId is null;
            var from = liked ? " FROM tracks t WHERE t.missing=0 AND t.favorite=1"
                : " FROM playlist_items pi JOIN tracks t ON t.id=pi.track_id JOIN playlists p ON p.id=pi.playlist_id WHERE pi.playlist_id=$id AND p.archived=0 AND t.missing=0";
            if (!liked) command.Parameters.AddWithValue("$id", query.PlaylistId!);
            from += Echo.Infrastructure.Search.LibrarySearchSql.Tracks(command, query.Search);
            command.CommandText = "SELECT count(*),coalesce(sum(t.duration),0)" + from;
            long total; double duration;
            using (var reader = command.ExecuteReader()) { reader.Read(); total = reader.GetInt64(0); duration = reader.GetDouble(1); }
            var sort = query.Sort switch
            {
                PlaylistTrackSort.Title => "t.sort_key", PlaylistTrackSort.Artist => "t.artist COLLATE NOCASE",
                PlaylistTrackSort.Album => "t.album COLLATE NOCASE", PlaylistTrackSort.Duration => "t.duration",
                PlaylistTrackSort.Quality => "t.sample_rate", _ => liked ? "t.sort_key" : "pi.position"
            };
            var direction = query.Descending ? " DESC" : " ASC";
            command.CommandText = "SELECT " + TrackRowMapping.AliasedColumns + (liked ? ",t.id,0" : ",pi.id,pi.position") + from
                + " ORDER BY " + sort + direction + (liked ? ",t.id" : ",pi.id") + direction + " LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$limit", query.Limit); command.Parameters.AddWithValue("$offset", query.Offset);
            var rows = new List<PlaylistTrackRow>(query.Limit);
            using (var reader = command.ExecuteReader())
                while (reader.Read()) { cancellation.ThrowIfCancellationRequested(); rows.Add(new(reader.GetString(14), reader.GetInt64(15), TrackRowMapping.Read(reader))); }
            transaction.Commit(); return new PlaylistTrackPage(rows, total, duration, revision);
        }, true, cancellation);
    }
    private static long PlaylistRevision(SqliteCommand command, long? expected)
    {
        command.CommandText = "SELECT revision FROM library_state WHERE id=1"; var revision = (long)command.ExecuteScalar()!;
        if (expected is { } value && value != revision) throw new LibraryChangedException();
        return revision;
    }
    private static void ValidatePlaylistPage(int offset, int limit, string? search)
    { if (offset is < 0 or > 1_000_000 || limit is < 1 or > 200 || search?.Length > 128) throw new ArgumentException("Invalid bounded playlist query."); }
    private static void ValidatePlaylistId(string id)
    { if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new ArgumentException("Invalid playlist ID."); }
}
