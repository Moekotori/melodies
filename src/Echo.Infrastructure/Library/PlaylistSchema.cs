using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class PlaylistSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE playlists(id TEXT PRIMARY KEY,name TEXT NOT NULL,description TEXT NOT NULL,
              archived INTEGER NOT NULL DEFAULT 0,updated_at INTEGER NOT NULL);
            CREATE INDEX playlists_name ON playlists(name COLLATE NOCASE,id) WHERE archived=0;
            CREATE TABLE playlist_items(id TEXT PRIMARY KEY,playlist_id TEXT NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
              track_id TEXT NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,position INTEGER NOT NULL);
            CREATE INDEX playlist_items_order ON playlist_items(playlist_id,position,id);
            CREATE INDEX playlist_items_track ON playlist_items(track_id);
            CREATE TABLE playlist_receipts(sequence INTEGER PRIMARY KEY AUTOINCREMENT,operation_id TEXT NOT NULL UNIQUE,
              fingerprint TEXT NOT NULL,result TEXT NOT NULL);
            CREATE INDEX tracks_liked ON tracks(sort_key,id) WHERE missing=0 AND favorite=1;
            PRAGMA user_version=11;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }
}
