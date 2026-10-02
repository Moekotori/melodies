using Echo.Contracts.Library;
using Echo.Infrastructure.Files;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task<IReadOnlyDictionary<string, KnownTrack>> FindKnownAsync(
        IReadOnlyList<FileSnapshot> files, CancellationToken cancellation)
    {
        if (files.Count > 256) throw new ArgumentException("Snapshot batch exceeds 256.");
        return _scanReader.QueueAsync<IReadOnlyDictionary<string, KnownTrack>>(connection =>
        {
            using var command = connection.CreateCommand();
            var placeholders = new string[files.Count];
            for (var i = 0; i < files.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                placeholders[i] = "$p" + i;
                command.Parameters.AddWithValue(placeholders[i], files[i].PathKey);
            }
            command.CommandText = "SELECT path_key,id,size_bytes,modified_ticks,parser_version,missing,path FROM tracks WHERE path_key IN (" + string.Join(',', placeholders) + ")";
            var found = new Dictionary<string, KnownTrack>(files.Count, StringComparer.Ordinal);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                found[reader.GetString(0)] = new(reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3),
                    reader.GetInt32(4), reader.GetBoolean(5), reader.GetString(6));
            }
            return found;
        }, false, cancellation);
    }

    public Task<TrackPage> GetTracksAsync(TrackQuery query, CancellationToken cancellation = default)
    {
        if (query.Limit is < 1 or > 200 || query.Search?.Length > 128 ||
            query.After?.SortKey.Length > 1024 || query.After?.Id.Length > 64)
            throw new ArgumentException("Invalid bounded page query.");
        return _interactiveReader.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM library_state WHERE id=1";
            var revision = (long)command.ExecuteScalar()!;
            if (query.After is { } cursor && cursor.Revision != revision) throw new LibraryChangedException();
            var search = FtsQuery(query.Search);
            command.CommandText = """
                SELECT t.id,t.path,t.title,t.artist,t.album,t.duration,t.codec,t.sample_rate,t.bit_depth,t.sort_key
                FROM tracks t
                """ + (search is null ? "" : " JOIN tracks_fts ON tracks_fts.rowid=t.rowid ") +
                " WHERE t.missing=0 " + (search is null ? "" : " AND tracks_fts MATCH $search ") +
                (query.After is null ? "" : " AND (t.sort_key,t.id)>($sort,$id) ") +
                " ORDER BY t.sort_key,t.id LIMIT $limit";
            if (search is not null) command.Parameters.AddWithValue("$search", search);
            if (query.After is { } after)
            {
                command.Parameters.AddWithValue("$sort", after.SortKey);
                command.Parameters.AddWithValue("$id", after.Id);
            }
            command.Parameters.AddWithValue("$limit", query.Limit + 1);
            var rows = new List<TrackRow>(query.Limit + 1);
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                        reader.GetString(4), reader.GetDouble(5), reader.GetString(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetString(9)));
                }
            var more = rows.Count > query.Limit;
            if (more) rows.RemoveAt(rows.Count - 1);
            TrackCursor? next = more ? new(rows[^1].SortKey, rows[^1].Id, revision) : null;
            transaction.Commit();
            return new TrackPage(rows, next, revision);
        }, true, cancellation);
    }

    public Task<LibraryStats> GetStatsAsync(CancellationToken cancellation = default) =>
        _interactiveReader.QueueAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*),COALESCE(SUM(missing),0),(SELECT revision FROM library_state WHERE id=1) FROM tracks";
            using var reader = command.ExecuteReader();
            reader.Read();
            return new LibraryStats(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
        }, true, cancellation);

    private static string? FtsQuery(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return null;
        var terms = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 16) throw new ArgumentException("Search has too many terms.");
        return string.Join(" AND ", terms.Select(term => "\"" + term.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"*"));
    }
}
