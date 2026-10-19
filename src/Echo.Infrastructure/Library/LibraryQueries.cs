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
            command.CommandText = "SELECT path_key,id,size_bytes,modified_ticks,parser_version,missing,path,dependency_stamp FROM tracks WHERE path_key IN (" + string.Join(',', placeholders) + ")";
            var found = new Dictionary<string, KnownTrack>(files.Count, StringComparer.Ordinal);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                found[reader.GetString(0)] = new(reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3),
                    reader.GetInt32(4), reader.GetBoolean(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7));
            }
            return found;
        }, false, cancellation);
    }

    public Task<TrackPage> GetTracksAsync(TrackQuery query, CancellationToken cancellation = default)
    {
        if (query.Limit is < 1 or > 200 || query.Search?.Length > 128 || query.Offset < 0 ||
            query.After?.SortKey.Length > 1024 || query.After?.Id.Length > 64 || query.After?.Offset < 0)
            throw new ArgumentException("Invalid bounded page query.");
        LibraryFilterSql.Validate(query.Filter);
        var orderSql = TrackOrdering.Sql(query.Order, query.RandomSeed);
        // Cursor identity includes all membership/order constraints; length-delimited values avoid collisions.
        static string Part(string? value) => (value?.Length ?? 0) + ":" + value;
        var filter = query.Filter ?? new();
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            Part(query.Search) + Part(orderSql) + $"{filter.Format}/{filter.Rate}/{filter.BitDepth}/{filter.Year}/{filter.FavoriteOnly}/{filter.LosslessOnly}/{filter.Source}/{filter.DuplicatesOnly}" + Part(filter.Genre) + Part(filter.Folder))));
        if (query.After?.QueryKey is { } expectedKey && expectedKey != key) throw new ArgumentException("Cursor belongs to a different query.");
        if (query.After is { QueryKey: null } && (query.Order is not null || LibraryFilterSql.Active(query.Filter)))
            throw new ArgumentException("Legacy cursor cannot change sorting or filtering.");
        return _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM library_state WHERE id=1";
            var revision = (long)command.ExecuteScalar()!;
            if ((query.After is { } cursor && cursor.Revision != revision) || (query.Revision is { } expected && expected != revision))
                throw new LibraryChangedException();
            var from = " FROM tracks t WHERE t.missing=0" + Echo.Infrastructure.Search.LibrarySearchSql.Tracks(command, query.Search)
                + LibraryFilterSql.Build(command, query.Filter);
            var offset = query.After?.Offset ?? query.Offset;
            // Preserve old title cursor compatibility, while arbitrary multi-sort uses revision-bound offsets.
            if (query.After is { } legacy && (legacy.QueryKey is null || orderSql == " ORDER BY t.sort_key ASC,t.id ASC"))
            {
                offset = 0;
                from += " AND (t.sort_key,t.id)>($sort,$id)";
                command.Parameters.AddWithValue("$sort", legacy.SortKey); command.Parameters.AddWithValue("$id", legacy.Id);
            }
            command.CommandText = """
                SELECT t.id,t.path,t.title,t.artist,coalesce((SELECT display_title FROM album_identity WHERE group_key=t.album_key),t.album),t.duration,t.codec,t.sample_rate,t.bit_depth,t.sort_key,t.track_no,t.disc_no,t.favorite,t.source_json,t.genre
                """ + from + orderSql + " LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$limit", query.Limit + 1); command.Parameters.AddWithValue("$offset", offset);
            var rows = new List<TrackRow>(query.Limit + 1);
            using (var reader = command.ExecuteReader())
                while (reader.Read()) { cancellation.ThrowIfCancellationRequested(); rows.Add(TrackRowMapping.Read(reader, withGenre: true)); }
            var more = rows.Count > query.Limit;
            if (more) rows.RemoveAt(rows.Count - 1);
            TrackCursor? next = more ? new(rows[^1].SortKey, rows[^1].Id, revision, checked((query.After?.Offset ?? query.Offset) + query.Limit), key) : null;
            transaction.Commit(); return new TrackPage(rows, next, revision);
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

}
