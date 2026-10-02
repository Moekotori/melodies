using Echo.Contracts.Library;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public async Task<long> ClearHistoryAsync(CancellationToken cancellation = default)
    {
        var result = await _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT count(*) FROM listening_events WHERE history_epoch=(SELECT epoch FROM history_state WHERE id=1)";
            var count = (long)command.ExecuteScalar()!;
            command.CommandText = "UPDATE history_state SET epoch=epoch+1 WHERE id=1 RETURNING epoch";
            var epoch = (long)command.ExecuteScalar()!;
            transaction.Commit(); return (count, epoch);
        }, true, cancellation).ConfigureAwait(false);
        // Epoch hides all cleared rows atomically and rejects late recorder writes. Cleanup yields every 200 rows.
        await DeleteHistoryBatchesAsync("history_epoch<$value", result.epoch, cancellation).ConfigureAwait(false);
        return result.count;
    }
    public async Task<long> PruneInvalidHistoryAsync(CancellationToken cancellation = default)
    {
        var snapshot = await _interactiveReader.QueueAsync(connection =>
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT coalesce(max(rowid),0) FROM listening_events";
            return (long)command.ExecuteScalar()!;
        }, true, cancellation).ConfigureAwait(false);
        return await DeleteHistoryBatchesAsync("rowid<=$value AND ended IS NOT NULL AND NOT EXISTS(SELECT 1 FROM tracks t WHERE t.id=listening_events.track_id AND t.missing=0)", snapshot, cancellation).ConfigureAwait(false);
    }
    private async Task<long> DeleteHistoryBatchesAsync(string predicate, long value, CancellationToken cancellation)
    {
        long count = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var deleted = await _writer.QueueAsync(connection =>
            {
                using var command = connection.CreateCommand(); command.Parameters.AddWithValue("$value", value);
                command.CommandText = "DELETE FROM listening_events WHERE rowid IN(SELECT rowid FROM listening_events WHERE " + predicate + " LIMIT 200)";
                return command.ExecuteNonQuery();
            }, false, cancellation).ConfigureAwait(false);
            count += deleted; if (deleted < 200) return count;
        }
    }
}
