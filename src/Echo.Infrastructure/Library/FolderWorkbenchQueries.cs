using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Search;
using Echo.Infrastructure.Files;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task<FolderPage> SearchFoldersAsync(string search, int offset = 0, int limit = 100, CancellationToken cancellation = default)
    {
        if (search.Length is < 1 or > 128 || offset is < 0 or > 100000 || limit is < 1 or > 200) throw new ArgumentException("Invalid directory search.");
        return _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation); using var command = connection.CreateCommand();
            command.CommandText = """
                WITH RECURSIVE directories(path) AS (
                  SELECT DISTINCT echo_directory(path) FROM tracks WHERE missing=0
                  UNION SELECT echo_directory(path) FROM directories WHERE echo_directory(path) IS NOT NULL AND echo_directory(path)<>path
                ), matches AS (
                  SELECT path,echo_folder_prefix(path) AS prefix FROM directories
                  WHERE echo_fold(path) LIKE $search ESCAPE '\' AND EXISTS(
                    SELECT 1 FROM library_roots r WHERE echo_folder_prefix(path)>=r.prefix
                      AND echo_folder_prefix(path)<substr(r.prefix,1,length(r.prefix)-1)||$upperSeparator)
                  ORDER BY echo_fold(path) LIMIT $limit OFFSET $offset)
                SELECT m.path,count(t.id),coalesce(sum(t.duration),0),coalesce(sum(t.size_bytes),0),min(t.path)
                FROM matches m LEFT JOIN tracks t ON t.missing=0 AND t.path_key>=m.prefix
                  AND t.path_key<substr(m.prefix,1,length(m.prefix)-1)||$upperSeparator
                GROUP BY m.path ORDER BY echo_fold(m.path)
                """;
            command.Parameters.AddWithValue("$search", "%" + LibrarySearchSql.Escape(LibrarySearchSchema.Fold(search.Trim())) + "%");
            command.Parameters.AddWithValue("$upperSeparator", ((char)(Path.DirectorySeparatorChar + 1)).ToString());
            command.Parameters.AddWithValue("$limit", limit + 1); command.Parameters.AddWithValue("$offset", offset);
            var rows = new List<FolderRow>(); using var reader = command.ExecuteReader();
            while (reader.Read())
            { cancellation.ThrowIfCancellationRequested(); var path = reader.GetString(0); var name = Path.GetFileName(path); rows.Add(new(path, name.Length == 0 ? path : name, reader.GetInt64(1), reader.GetDouble(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetString(4))); }
            var more = rows.Count > limit; if (more) rows.RemoveAt(rows.Count - 1); return new FolderPage(rows, more);
        }, true, cancellation);
    }

    public Task<FolderInsights> GetFolderInsightsAsync(FolderTrackQuery query, CancellationToken cancellation = default) =>
        _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation); using var command = connection.CreateCommand();
            var scope = FolderTrackSql.Scope(command, query); var duplicates = FolderTrackSql.Peer(command, query, false);
            command.CommandText = "SELECT coalesce(sum(t.created_at>=unixepoch()-30*86400),0),coalesce(sum(" + FolderTrackSql.Plays + "=0),0),coalesce(sum(" + FolderTrackSql.Lossless + "),0),coalesce(sum(" + FolderTrackSql.HiRes + "),0)," +
                "coalesce(sum(f.cover=0),0),coalesce(sum(f.lyrics=0),0),coalesce(sum(" + duplicates + "),0),coalesce(sum(f.track_id IS NULL),0)" +
                " FROM tracks t LEFT JOIN folder_resource_facts f ON f.track_id=t.id WHERE t.missing=0 AND " + scope;
            using var reader = command.ExecuteReader(); reader.Read();
            return new FolderInsights(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7));
        }, true, cancellation);

    public Task<IReadOnlyList<FolderScanRecord>> GetFolderScanHistoryAsync(string? path, CancellationToken cancellation = default) =>
        _interactiveReader.QueueAsync<IReadOnlyList<FolderScanRecord>>(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation); using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,root_prefix,state,discovered,processed,inserted,updated,unchanged,failed,marked_missing,started_at,finished_at FROM scan_jobs" +
                (path is null ? "" : " WHERE root_prefix=$root") + " ORDER BY started_at DESC,id DESC LIMIT 32";
            if (path is not null) command.Parameters.AddWithValue("$root", LibraryPath.RootPrefix(path));
            var rows = new List<FolderScanRecord>(); using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                rows.Add(new(reader.GetString(0), reader.GetString(1), Enum.TryParse<ScanStatus>(reader.GetString(2), out var status) ? status : ScanStatus.Interrupted,
                    reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9), reader.GetInt64(10), reader.IsDBNull(11) ? null : reader.GetInt64(11)));
            }
            return rows;
        }, true, cancellation);

    public Task StoreFolderResourcesAsync(IReadOnlyList<FolderResourceFact> facts, CancellationToken cancellation = default)
    {
        if (facts.Count is < 1 or > 200) throw new ArgumentException("Resource batch exceeds 200."); var owned = facts.ToArray();
        return _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO folder_resource_facts(track_id,cover,lyrics,checked) SELECT $id,$cover,$lyrics,unixepoch() WHERE EXISTS(SELECT 1 FROM tracks WHERE id=$id) ON CONFLICT(track_id) DO UPDATE SET cover=excluded.cover,lyrics=excluded.lyrics,checked=excluded.checked";
            command.Parameters.AddWithValue("$id", ""); command.Parameters.AddWithValue("$cover", false); command.Parameters.AddWithValue("$lyrics", false);
            foreach (var fact in owned) { cancellation.ThrowIfCancellationRequested(); command.Parameters[0].Value = fact.TrackId; command.Parameters[1].Value = fact.Cover; command.Parameters[2].Value = fact.Lyrics; command.ExecuteNonQuery(); }
            transaction.Commit(); return true;
        }, false, cancellation);
    }
}
