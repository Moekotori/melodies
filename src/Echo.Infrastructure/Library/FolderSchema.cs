using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class FolderSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE library_roots(prefix TEXT PRIMARY KEY) WITHOUT ROWID;
            INSERT OR IGNORE INTO library_roots SELECT DISTINCT root_prefix FROM scan_jobs;
            PRAGMA user_version=9;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }
}
