using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;
using Echo.Infrastructure.Files;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : IHomeLibrary
{
    private const string HomeAlbumColumns = """
        i.stable_id,i.display_title,i.display_artist,a.track_count,a.duration,a.year,
        (SELECT path FROM tracks WHERE album_key=a.id AND missing=0 ORDER BY disc_no,track_no,sort_key,id LIMIT 1),
        a.lossless_count,a.added,coalesce(u.favorite,0),coalesce(u.rating,0)
        """;
    private const string HomeAlbumsFrom = " FROM albums a JOIN album_identity i ON i.group_key=a.id LEFT JOIN album_user_state u ON u.album_id=a.id ";

    public Task<HomeDashboard> GetHomeAsync(DateOnly today, CancellationToken cancellation = default)
        => _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            connection.CreateFunction<string, string>("echo_home_folder", path => Path.GetDirectoryName(path) ?? "", isDeterministic: true);
            command.CommandText = """
                SELECT (SELECT count(*) FROM artists WHERE grouping=0),
                  (SELECT count(*) FROM albums),(SELECT count(*) FROM tracks WHERE missing=0),
                  (SELECT count(DISTINCT echo_home_folder(path)) FROM tracks WHERE missing=0)
                """;
            HomeLibrarySummary summary;
            using (var reader = command.ExecuteReader())
            { reader.Read(); summary = new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)); }
            var recent = ReadHomeAlbums(command, " ORDER BY a.added DESC,a.id LIMIT 8", cancellation);
            var offset = summary.Albums <= 7 ? 0 : Random.Shared.NextInt64(summary.Albums - 6);
            command.Parameters.AddWithValue("$offset", offset);
            var recommendations = ReadHomeAlbums(command, " ORDER BY i.title_key,i.stable_id LIMIT 7 OFFSET $offset", cancellation);
            command.Parameters.Clear();
            var played = ReadHomeAlbums(command, " WHERE a.id IN(SELECT t.album_key FROM (SELECT * FROM listening_events WHERE history_epoch=(SELECT epoch FROM history_state WHERE id=1)) e JOIN tracks t ON t.id=e.track_id WHERE t.missing=0 AND e.history_epoch=(SELECT epoch FROM history_state WHERE id=1) GROUP BY t.album_key ORDER BY max(e.started) DESC LIMIT 8) ORDER BY (SELECT max(e.started) FROM (SELECT * FROM listening_events WHERE history_epoch=(SELECT epoch FROM history_state WHERE id=1)) e JOIN tracks t ON t.id=e.track_id WHERE t.album_key=a.id AND e.history_epoch=(SELECT epoch FROM history_state WHERE id=1)) DESC,a.id", cancellation);
            command.CommandText = "SELECT " + TrackRowMapping.AliasedColumns + " FROM tracks t WHERE t.missing=0 AND t.id=(SELECT e.track_id FROM (SELECT * FROM listening_events WHERE history_epoch=(SELECT epoch FROM history_state WHERE id=1)) e JOIN tracks present ON present.id=e.track_id AND present.missing=0 WHERE e.history_epoch=(SELECT epoch FROM history_state WHERE id=1) ORDER BY e.started DESC,e.id DESC LIMIT 1)";
            TrackRow? last;
            using (var reader = command.ExecuteReader()) last = reader.Read() ? TrackRowMapping.Read(reader) : null;
            var listening = ReadHomeListening(command, today, cancellation);
            transaction.Commit();
            return new HomeDashboard(summary, recent, played, recommendations, last, listening);
        }, true, cancellation);

    private static List<AlbumRow> ReadHomeAlbums(SqliteCommand command, string suffix, CancellationToken token)
    {
        command.CommandText = "SELECT " + HomeAlbumColumns + HomeAlbumsFrom + suffix;
        var rows = new List<AlbumRow>(8);
        using var reader = command.ExecuteReader();
        while (reader.Read()) { token.ThrowIfCancellationRequested(); rows.Add(ReadHomeAlbum(reader)); }
        return rows;
    }
    private static AlbumRow ReadHomeAlbum(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2),
        r.GetInt32(3), r.GetDouble(4), r.IsDBNull(5) ? null : r.GetInt32(5), r.GetString(6), r.GetInt32(7),
        r.GetInt64(8), r.GetBoolean(9), r.GetInt32(10));

    public Task<AlbumRow?> GetTrackAlbumAsync(string path, CancellationToken cancellation = default)
    {
        if (path.Length > 32768) throw new ArgumentException("Invalid path.");
        return _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("$path", LibraryPath.Key(LibraryPath.Canonical(path)));
            command.CommandText = "SELECT " + HomeAlbumColumns + HomeAlbumsFrom + " WHERE a.id=(SELECT album_key FROM tracks WHERE path_key=$path AND missing=0) LIMIT 1";
            using var reader = command.ExecuteReader(); return reader.Read() ? ReadHomeAlbum(reader) : null;
        }, true, cancellation);
    }

    public Task<IReadOnlyList<TrackRow>> GetRandomTracksAsync(CancellationToken cancellation = default)
        => _interactiveReader.QueueAsync<IReadOnlyList<TrackRow>>(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT coalesce(max(rowid),0) FROM tracks";
            var max = (long)command.ExecuteScalar()!;
            var pivot = max == 0 ? 0 : Random.Shared.NextInt64(max) + 1;
            command.Parameters.AddWithValue("$pivot", pivot);
            var rows = new List<TrackRow>(36);
            foreach (var range in new[] { "rowid >= $pivot", "rowid < $pivot" })
            {
                if (command.Parameters.Contains("$limit")) command.Parameters.RemoveAt("$limit");
                command.Parameters.AddWithValue("$limit", 36 - rows.Count);
                command.CommandText = "SELECT " + TrackRowMapping.Columns + " FROM tracks WHERE missing=0 AND " + range + " ORDER BY rowid LIMIT $limit";
                using var reader = command.ExecuteReader();
                while (reader.Read()) { cancellation.ThrowIfCancellationRequested(); rows.Add(TrackRowMapping.Read(reader)); }
                if (rows.Count == 36) break;
            }
            transaction.Commit(); Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows)); return rows;
        }, true, cancellation);

    public Task RecordListeningAsync(ListeningEntry entry, CancellationToken cancellation = default)
    {
        if (entry.Id.Length is < 1 or > 64 || entry.TrackId.Length > 1100 || entry.Path.Length > 32768 ||
            !double.IsFinite(entry.PlayedSeconds) || entry.PlayedSeconds < 0 || entry.StartedUnix < 0 || entry.UpdatedUnix < entry.StartedUnix ||
            entry.HistoryEpoch < 0 || entry.EndedUnix < entry.StartedUnix || entry.PlayedSeconds > TimeSpan.MaxValue.TotalSeconds ||
            entry.UpdatedUnix > 253402300799 || entry.EndedUnix > 253402300799)
            throw new ArgumentException("Invalid listening entry.");
        return _writer.QueueAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO listening_events(id,track_id,started,updated,seconds,ended,title,artist,album,path,duration,codec,source,history_epoch,counted,sample_rate,bit_depth)
                SELECT $id,t.id,$start,$updated,$seconds,$ended,t.title,t.artist,t.album,t.path,t.duration,t.codec,$source,s.epoch,
                  $seconds>=30 OR (t.duration>0 AND $seconds>=t.duration*0.5),t.sample_rate,t.bit_depth
                FROM tracks t CROSS JOIN history_state s WHERE (t.id=$track OR t.path_key=$path) AND ($epoch IS NULL OR $epoch=s.epoch)
                ORDER BY t.id=$track DESC LIMIT 1
                ON CONFLICT(id) DO UPDATE SET updated=max(updated,excluded.updated),seconds=max(seconds,excluded.seconds),
                  ended=coalesce(ended,excluded.ended),counted=max(counted,excluded.counted);
                """;
            command.Parameters.AddWithValue("$id", entry.Id); command.Parameters.AddWithValue("$track", entry.TrackId);
            command.Parameters.AddWithValue("$path", LibraryPath.Key(LibraryPath.Canonical(entry.Path)));
            command.Parameters.AddWithValue("$start", entry.StartedUnix); command.Parameters.AddWithValue("$updated", entry.UpdatedUnix);
            command.Parameters.AddWithValue("$seconds", entry.PlayedSeconds);
            command.Parameters.AddWithValue("$ended", (object?)entry.EndedUnix ?? DBNull.Value);
            command.Parameters.AddWithValue("$source", entry.Source.Length > 256 ? entry.Source[..256] : entry.Source);
            command.Parameters.AddWithValue("$epoch", (object?)entry.HistoryEpoch ?? DBNull.Value);
            return command.ExecuteNonQuery();
        }, true, cancellation);
    }
}
