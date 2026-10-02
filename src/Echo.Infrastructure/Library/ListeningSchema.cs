using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class ListeningSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE listening_events(id TEXT PRIMARY KEY,track_id TEXT NOT NULL,
              started INTEGER NOT NULL,updated INTEGER NOT NULL,seconds REAL NOT NULL CHECK(seconds>=0));
            CREATE INDEX listening_started ON listening_events(started DESC,track_id);
            CREATE INDEX listening_track ON listening_events(track_id,started DESC);
            PRAGMA user_version=10;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }
}
