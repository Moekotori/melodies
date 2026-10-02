using Echo.Contracts.Library;
using Echo.Infrastructure.Files;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : IFolderLibrary
{
    private const string FolderLossless = "lower(t.codec) IN ('flac','alac','wav','wave','aiff','aif','ape','wv','tta','dsf','dff')";

    public Task<FolderPage> GetFoldersAsync(FolderQuery query, CancellationToken cancellation = default)
    {
        if (query.Limit is < 1 or > 200 || query.Offset is < 0 or > 1_000_000)
            throw new ArgumentException("Invalid folder page.");
        var prefix = query.ParentPath is null ? null : LibraryPath.RootPrefix(query.ParentPath);
        return _interactiveReader.QueueAsync(connection =>
        {
            using var command = connection.CreateCommand();
            using var interrupt = new ReadCancellation(connection, cancellation);
            command.Parameters.AddWithValue("$limit", query.Limit + 1);
            command.Parameters.AddWithValue("$offset", query.Offset);
            command.Parameters.AddWithValue("$separator", Path.DirectorySeparatorChar.ToString());
            if (prefix is null)
            {
                // Persisted roots outlive the bounded scan history. Nested imports share their ancestor root.
                command.CommandText = """
                    SELECT coalesce(substr(min(t.path),1,length(r.prefix)),r.prefix),count(t.id),
                      coalesce(sum(t.duration),0),coalesce(sum(t.size_bytes),0),min(t.path)
                    FROM library_roots r LEFT JOIN tracks t ON t.missing=0
                      AND t.path_key>=r.prefix AND t.path_key<substr(r.prefix,1,length(r.prefix)-1)||$upperSeparator
                    WHERE NOT EXISTS(SELECT 1 FROM library_roots ancestor
                      WHERE length(ancestor.prefix)<length(r.prefix)
                        AND substr(r.prefix,1,length(ancestor.prefix))=ancestor.prefix)
                    GROUP BY r.prefix ORDER BY r.prefix LIMIT $limit OFFSET $offset
                    """;
                command.Parameters.AddWithValue("$upperSeparator", ((char)(Path.DirectorySeparatorChar + 1)).ToString());
            }
            else
            {
                AddFolderRange(command, prefix);
                command.CommandText = """
                    WITH descendants AS (
                      SELECT t.*,instr(substr(t.path_key,$start),$separator) AS boundary
                      FROM tracks t WHERE t.missing=0 AND t.path_key>=$lo AND t.path_key<$hi)
                    SELECT substr(min(path),1,$start+boundary-2),count(*),sum(duration),sum(size_bytes),min(path)
                    FROM descendants WHERE boundary>0
                    GROUP BY substr(path_key,1,$start+boundary-2)
                    ORDER BY substr(path_key,1,$start+boundary-2) LIMIT $limit OFFSET $offset
                    """;
            }
            var rows = new List<FolderRow>(query.Limit + 1);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                var path = Path.TrimEndingDirectorySeparator(reader.GetString(0));
                var name = Path.GetFileName(path);
                rows.Add(new(path, string.IsNullOrEmpty(name) ? path : name, reader.GetInt64(1),
                    reader.GetDouble(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
            var more = rows.Count > query.Limit;
            if (more) rows.RemoveAt(rows.Count - 1);
            return new FolderPage(rows, more);
        }, true, cancellation);
    }

    public Task<FolderTrackPage> GetFolderTracksAsync(FolderTrackQuery query, CancellationToken cancellation = default)
    {
        if (query.Limit is < 1 or > 200 || query.Offset is < 0 or > 10_000_000 || query.Search?.Length > 128
            || !Enum.IsDefined(query.Sort)) throw new ArgumentException("Invalid bounded folder query.");
        var prefix = LibraryPath.RootPrefix(query.Path);
        return _interactiveReader.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            using var interrupt = new ReadCancellation(connection, cancellation);
            command.CommandText = "SELECT revision FROM library_state WHERE id=1";
            var revision = (long)command.ExecuteScalar()!;
            if (query.Revision is { } expected && expected != revision) throw new LibraryChangedException();
            AddFolderRange(command, prefix);
            command.Parameters.AddWithValue("$separator", Path.DirectorySeparatorChar.ToString());
            var where = " FROM tracks t WHERE t.missing=0 AND t.path_key>=$lo AND t.path_key<$hi" +
                (query.Recursive ? "" : " AND instr(substr(t.path_key,$start),$separator)=0") +
                (query.LosslessOnly ? " AND " + FolderLossless : "") +
                Echo.Infrastructure.Search.LibrarySearchSql.Tracks(command, query.Search);
            command.CommandText = "SELECT count(*),coalesce(sum(t.duration),0),coalesce(sum(t.size_bytes),0),coalesce(sum(" + FolderLossless + "),0)" + where;
            long total, bytes, lossless; double duration;
            using (var reader = command.ExecuteReader())
            { reader.Read(); total = reader.GetInt64(0); duration = reader.GetDouble(1); bytes = reader.GetInt64(2); lossless = reader.GetInt64(3); }
            cancellation.ThrowIfCancellationRequested();
            var sort = query.Sort switch
            {
                FolderTrackSort.Title => "t.sort_key", FolderTrackSort.Artist => "t.artist COLLATE NOCASE",
                FolderTrackSort.Album => "t.album COLLATE NOCASE", FolderTrackSort.Duration => "t.duration", _ => "t.path_key"
            };
            command.CommandText = "SELECT " + TrackRowMapping.AliasedColumns + where + " ORDER BY " + sort +
                (query.Descending ? " DESC,t.id DESC" : ",t.id") + " LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$limit", query.Limit);
            command.Parameters.AddWithValue("$offset", query.Offset);
            var rows = new List<TrackRow>(query.Limit);
            using (var reader = command.ExecuteReader())
                while (reader.Read()) { cancellation.ThrowIfCancellationRequested(); rows.Add(TrackRowMapping.Read(reader)); }
            transaction.Commit();
            return new FolderTrackPage(rows, total, revision, duration, bytes, lossless);
        }, true, cancellation);
    }

    private static void AddFolderRange(SqliteCommand command, string prefix)
    {
        command.Parameters.AddWithValue("$lo", prefix);
        command.Parameters.AddWithValue("$hi", prefix[..^1] + (char)(prefix[^1] + 1));
        command.Parameters.AddWithValue("$start", prefix.Length + 1);
    }
}
