using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;
internal static class VirtualAudioIdentitySchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE tracks ADD COLUMN physical_path TEXT;
            ALTER TABLE tracks ADD COLUMN source_kind TEXT;
            CREATE INDEX tracks_source_audio ON tracks(physical_path,source_kind);
            CREATE INDEX source_cache_recent ON scan_source_cache(updated_at);
            PRAGMA user_version=7;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }
}
