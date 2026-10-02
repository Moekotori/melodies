using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class HistoryQualitySchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE listening_events ADD COLUMN sample_rate INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE listening_events ADD COLUMN bit_depth INTEGER NOT NULL DEFAULT 0;
            UPDATE listening_events SET (sample_rate,bit_depth)=(SELECT sample_rate,bit_depth FROM tracks WHERE tracks.id=listening_events.track_id)
              WHERE EXISTS(SELECT 1 FROM tracks WHERE tracks.id=listening_events.track_id);
            PRAGMA user_version=16;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }
}
