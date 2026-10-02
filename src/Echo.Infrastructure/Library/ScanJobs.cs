using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task<string> BeginScanAsync(string root, bool trackPresence, CancellationToken cancellation)
    {
        var prefix = LibraryPath.RootPrefix(root);
        return _writer.QueueAsync(connection =>
        {
            var id = Guid.CreateVersion7().ToString("N");
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // Retain a bounded history, never remove an active job.
            command.CommandText = """
                DELETE FROM scan_errors WHERE scan_id IN(
                  SELECT id FROM scan_jobs WHERE state!='Running' ORDER BY started_at DESC LIMIT -1 OFFSET 31);
                DELETE FROM scan_jobs WHERE state!='Running' AND id NOT IN(
                  SELECT id FROM scan_jobs WHERE state!='Running' ORDER BY started_at DESC LIMIT 31);
                INSERT INTO scan_jobs(id,root_prefix,state,track_presence,started_at)
                VALUES($id,$root,'Running',$presence,unixepoch());
                INSERT OR IGNORE INTO library_roots(prefix) VALUES($root);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$root", prefix);
            command.Parameters.AddWithValue("$presence", trackPresence);
            command.ExecuteNonQuery();
            cancellation.ThrowIfCancellationRequested();
            transaction.Commit();
            return id;
        }, true, cancellation);
    }

    public Task<long> FinishScanAsync(string jobId, ScanStatus status, long discovered,
        bool detectMissing, string? errorCode, CancellationToken cancellation) =>
        _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("$id", jobId);
            command.CommandText = "SELECT root_prefix,failed,track_presence FROM scan_jobs WHERE id=$id AND state='Running'";
            string prefix;
            long failed;
            bool presence;
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read()) throw new InvalidOperationException("Scan is not active.");
                prefix = reader.GetString(0);
                failed = reader.GetInt64(1);
                presence = reader.GetBoolean(2);
            }
            long missing = 0;
            if (status == ScanStatus.Completed && failed == 0 && detectMissing && presence)
            {
                // A lexical prefix range uses the path_key index; no full-library path array.
                var upper = prefix[..^1] + (char)(prefix[^1] + 1);
                command.Parameters.AddWithValue("$lo", prefix);
                command.Parameters.AddWithValue("$hi", upper);
                command.CommandText = """
                    UPDATE tracks SET missing=1
                    WHERE missing=0 AND path_key >= $lo AND path_key < $hi
                    AND NOT EXISTS(SELECT 1 FROM scan_seen s WHERE s.scan_id=$id AND s.path_key=tracks.path_key);
                    """;
                missing = command.ExecuteNonQuery();
                if (missing > 0)
                {
                    command.CommandText = "UPDATE library_state SET revision=revision+1 WHERE id=1";
                    command.ExecuteNonQuery();
                }
            }
            command.Parameters.AddWithValue("$state", status.ToString());
            command.Parameters.AddWithValue("$discovered", discovered);
            command.Parameters.AddWithValue("$missing", missing);
            command.Parameters.AddWithValue("$error", (object?)errorCode ?? DBNull.Value);
            command.CommandText = """
                UPDATE scan_jobs SET state=$state,discovered=$discovered,marked_missing=$missing,
                  error_code=$error,finished_at=unixepoch() WHERE id=$id;
                DELETE FROM scan_seen WHERE scan_id=$id;
                """;
            command.ExecuteNonQuery();
            cancellation.ThrowIfCancellationRequested();
            transaction.Commit();
            return missing;
        }, true, cancellation);
}
