using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class FolderWorkbenchSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE folder_rules(path_key TEXT PRIMARY KEY,path TEXT NOT NULL,pinned INTEGER NOT NULL DEFAULT 0,
              position INTEGER NOT NULL DEFAULT 0,combined INTEGER NOT NULL DEFAULT 1,recursive INTEGER NOT NULL DEFAULT 1,
              sort INTEGER NOT NULL DEFAULT 0,descending INTEGER NOT NULL DEFAULT 0,watch INTEGER NOT NULL DEFAULT 0,
              hide_duplicates INTEGER NOT NULL DEFAULT 0,ignore_json TEXT NOT NULL DEFAULT '[]') WITHOUT ROWID;
            CREATE TABLE folder_resource_facts(track_id TEXT PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,
              cover INTEGER NOT NULL,lyrics INTEGER NOT NULL,checked INTEGER NOT NULL) WITHOUT ROWID;
            CREATE TABLE folder_hidden_tracks(track_id TEXT PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,
              was_missing INTEGER NOT NULL) WITHOUT ROWID;
            CREATE TABLE track_user_tags(track_id TEXT PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,
              title TEXT NOT NULL,artist TEXT NOT NULL,album TEXT NOT NULL,album_artist TEXT NOT NULL,
              track_no INTEGER,disc_no INTEGER,year INTEGER,genre TEXT NOT NULL) WITHOUT ROWID;
            CREATE TABLE folder_ui_state(id INTEGER PRIMARY KEY CHECK(id=1),last_path TEXT,width REAL NOT NULL,
              tree_compact INTEGER NOT NULL,tracks_compact INTEGER NOT NULL,expanded TEXT NOT NULL,selected TEXT NOT NULL);
            PRAGMA user_version=17;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }
}
