using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class HistorySchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE listening_events ADD COLUMN ended INTEGER;
            ALTER TABLE listening_events ADD COLUMN title TEXT NOT NULL DEFAULT '';
            ALTER TABLE listening_events ADD COLUMN artist TEXT NOT NULL DEFAULT '';
            ALTER TABLE listening_events ADD COLUMN album TEXT NOT NULL DEFAULT '';
            ALTER TABLE listening_events ADD COLUMN path TEXT NOT NULL DEFAULT '';
            ALTER TABLE listening_events ADD COLUMN duration REAL NOT NULL DEFAULT 0;
            ALTER TABLE listening_events ADD COLUMN codec TEXT NOT NULL DEFAULT '';
            ALTER TABLE listening_events ADD COLUMN source TEXT NOT NULL DEFAULT '';
            ALTER TABLE listening_events ADD COLUMN history_epoch INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE listening_events ADD COLUMN counted INTEGER NOT NULL DEFAULT 0;
            UPDATE listening_events SET ended=updated,
              (title,artist,album,path,duration,codec)=(SELECT title,artist,album,path,duration,codec FROM tracks WHERE tracks.id=listening_events.track_id)
              WHERE EXISTS(SELECT 1 FROM tracks WHERE tracks.id=listening_events.track_id);
            UPDATE listening_events SET counted=seconds>=30 OR (duration>0 AND seconds>=duration*0.5);
            UPDATE listening_events SET ended=updated WHERE ended IS NULL;
            CREATE TABLE history_state(id INTEGER PRIMARY KEY CHECK(id=1),epoch INTEGER NOT NULL);
            INSERT INTO history_state VALUES(1,0);
            CREATE INDEX history_page ON listening_events(history_epoch,started DESC,id DESC);
            CREATE INDEX history_counted_page ON listening_events(history_epoch,started DESC,id DESC) WHERE counted=1 AND ended IS NOT NULL;
            CREATE VIRTUAL TABLE history_search USING fts5(title,artist,album,tokenize='trigram');
            INSERT INTO history_search(rowid,title,artist,album) SELECT rowid,echo_fold(title),echo_fold(artist),echo_fold(album) FROM listening_events;
            CREATE TRIGGER history_search_insert AFTER INSERT ON listening_events BEGIN
              INSERT INTO history_search(rowid,title,artist,album) VALUES(new.rowid,echo_fold(new.title),echo_fold(new.artist),echo_fold(new.album)); END;
            CREATE TRIGGER history_search_delete AFTER DELETE ON listening_events BEGIN DELETE FROM history_search WHERE rowid=old.rowid; END;
            PRAGMA user_version=14;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }
}
