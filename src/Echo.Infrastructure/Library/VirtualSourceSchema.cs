using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class VirtualSourceSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE tracks ADD COLUMN source_json TEXT;
            ALTER TABLE tracks ADD COLUMN dependency_stamp TEXT;
            CREATE TABLE scan_source_cache(
              path_key TEXT PRIMARY KEY,size_bytes INTEGER NOT NULL,modified_ticks INTEGER NOT NULL,
              version INTEGER NOT NULL,json TEXT NOT NULL CHECK(length(json)<=16384),updated_at INTEGER NOT NULL);
            PRAGMA user_version=5;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
