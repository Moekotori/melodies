using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    // Only touched on the writer lane. At most 4096 new rows can accumulate between maintenance passes.
    private int _sourceCacheWritesSinceMaintenance;
    private bool _sourceCacheMaintained;
    private const int SourceCacheRows = 262144;
    public Task<IReadOnlyDictionary<string, SourceCacheItem>> ReadSourceCacheAsync(
        IReadOnlyList<FileSnapshot> files, CancellationToken cancellation)
    {
        if (files.Count > 128) throw new ArgumentException("Source cache lookup exceeds 128.");
        return _scanReader.QueueAsync<IReadOnlyDictionary<string, SourceCacheItem>>(connection =>
        {
            var result = new Dictionary<string, SourceCacheItem>(StringComparer.Ordinal);
            if (files.Count == 0) return result;
            using var command = connection.CreateCommand();
            var parameters = new string[files.Count];
            for (var i = 0; i < files.Count; i++)
            { parameters[i] = "$p" + i; command.Parameters.AddWithValue(parameters[i], files[i].PathKey); }
            command.CommandText = "SELECT path_key,size_bytes,modified_ticks,json FROM scan_source_cache WHERE version=1 AND path_key IN (" + string.Join(',', parameters) + ")";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                var item = new SourceCacheItem(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3));
                result[item.PathKey] = item;
            }
            return result;
        }, false, cancellation);
    }

    public Task StoreSourceCacheAsync(IReadOnlyList<SourceCacheItem> items, CancellationToken cancellation)
    {
        if (items.Count > 128 || items.Any(item => item.Json.Length > 16384))
            throw new ArgumentException("Source cache payload exceeds its limit.");
        if (items.Count == 0) return Task.CompletedTask;
        return _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO scan_source_cache(path_key,size_bytes,modified_ticks,version,json,updated_at)
                VALUES($path,$size,$ticks,1,$json,unixepoch())
                ON CONFLICT(path_key) DO UPDATE SET size_bytes=excluded.size_bytes,
                  modified_ticks=excluded.modified_ticks,version=1,json=excluded.json,updated_at=excluded.updated_at
                """;
            command.Parameters.Add(new("$path", SqliteType.Text)); command.Parameters.Add(new("$size", SqliteType.Integer));
            command.Parameters.Add(new("$ticks", SqliteType.Integer)); command.Parameters.Add(new("$json", SqliteType.Text));
            command.Prepare();
            foreach (var item in items)
            {
                cancellation.ThrowIfCancellationRequested();
                command.Parameters[0].Value = item.PathKey; command.Parameters[1].Value = item.SizeBytes;
                command.Parameters[2].Value = item.ModifiedTicks; command.Parameters[3].Value = item.Json;
                command.ExecuteNonQuery();
            }
            var pending = _sourceCacheWritesSinceMaintenance + items.Count;
            var maintain = !_sourceCacheMaintained || pending >= 4096;
            if (maintain)
            {
                using var prune = connection.CreateCommand();
                prune.Transaction = transaction;
                prune.CommandText = "SELECT count(*) FROM scan_source_cache";
                var excess = (long)prune.ExecuteScalar()! - SourceCacheRows;
                if (excess > 0)
                {
                    // Walk only the oldest excess via source_cache_recent, never OFFSET through the retained cache.
                    prune.CommandText = "DELETE FROM scan_source_cache WHERE rowid IN (SELECT rowid FROM scan_source_cache ORDER BY updated_at LIMIT $excess)";
                    prune.Parameters.AddWithValue("$excess", excess);
                    prune.ExecuteNonQuery();
                }
            }
            cancellation.ThrowIfCancellationRequested();
            transaction.Commit();
            _sourceCacheWritesSinceMaintenance = maintain ? 0 : pending;
            _sourceCacheMaintained = true;
            return true;
        }, false, cancellation);
    }
}
