using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// History owns qualification/epoch; album browsing keeps only its indexed derived facts.
internal static class AlbumListeningSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            DROP TRIGGER IF EXISTS listening_browse_insert;
            DROP TRIGGER IF EXISTS listening_browse_delete;
            DROP TRIGGER IF EXISTS listening_browse_update;
            DROP TRIGGER IF EXISTS albums_browse_insert;
            DROP TRIGGER IF EXISTS tracks_browse_insert;
            DROP TRIGGER IF EXISTS tracks_browse_delete;
            DROP TRIGGER IF EXISTS tracks_browse_update;
            """ + Refresh("1=1") + """
            CREATE TRIGGER albums_browse_insert AFTER INSERT ON albums BEGIN
              UPDATE albums SET shuffle_key=hex(randomblob(8)) WHERE id=new.id;
            """ + AlbumBrowseSchema.RefreshDates("id=new.id") + Refresh("id=new.id") + """
            END;
            CREATE TRIGGER tracks_browse_insert AFTER INSERT ON tracks WHEN new.missing=0 BEGIN
            """ + AlbumBrowseSchema.RefreshDates("id=new.album_key") + Refresh("id=new.album_key") + """
            END;
            CREATE TRIGGER tracks_browse_delete AFTER DELETE ON tracks WHEN old.missing=0 BEGIN
            """ + AlbumBrowseSchema.RefreshDates("id=old.album_key") + Refresh("id=old.album_key") + """
            END;
            CREATE TRIGGER tracks_browse_update AFTER UPDATE OF album,album_artist,artist,missing ON tracks
            WHEN old.album_key IS NOT new.album_key OR old.missing IS NOT new.missing BEGIN
            """ + AlbumBrowseSchema.RefreshDates("id IN(old.album_key,new.album_key)") + Refresh("id IN(old.album_key,new.album_key)") + """
            END;
            CREATE TRIGGER listening_browse_insert AFTER INSERT ON listening_events
            WHEN new.history_epoch=(SELECT epoch FROM history_state WHERE id=1) BEGIN
              UPDATE albums SET play_count=play_count+(new.counted=1 AND new.ended IS NOT NULL),last_played=max(last_played,new.started)
                WHERE id=(SELECT album_key FROM tracks WHERE id=new.track_id AND missing=0);
              UPDATE library_state SET revision=revision+1 WHERE id=1;
            END;
            CREATE TRIGGER listening_browse_delete AFTER DELETE ON listening_events
            WHEN old.history_epoch=(SELECT epoch FROM history_state WHERE id=1) BEGIN
            """ + Refresh("id=(SELECT album_key FROM tracks WHERE id=old.track_id)") + """
              UPDATE library_state SET revision=revision+1 WHERE id=1;
            END;
            CREATE TRIGGER listening_browse_update AFTER UPDATE OF counted,ended,track_id,started,history_epoch ON listening_events
            WHEN (old.counted=1 AND old.ended IS NOT NULL) IS NOT (new.counted=1 AND new.ended IS NOT NULL)
              OR old.track_id IS NOT new.track_id OR old.started IS NOT new.started OR old.history_epoch IS NOT new.history_epoch BEGIN
            """ + Refresh("id IN(SELECT album_key FROM tracks WHERE id IN(old.track_id,new.track_id))") + """
              UPDATE library_state SET revision=revision+1 WHERE id=1;
            END;
            CREATE TRIGGER history_browse_clear AFTER UPDATE OF epoch ON history_state WHEN old.epoch IS NOT new.epoch BEGIN
              UPDATE albums SET play_count=0,last_played=0;
              UPDATE library_state SET revision=revision+1 WHERE id=1;
            END;
            PRAGMA user_version=15;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }
    private static string Refresh(string predicate) => "\n" + $"""
        UPDATE albums SET
          play_count=(SELECT count(*) FROM tracks t JOIN listening_events e ON e.track_id=t.id
            WHERE t.album_key=albums.id AND t.missing=0 AND e.counted=1 AND e.ended IS NOT NULL
              AND e.history_epoch=(SELECT epoch FROM history_state WHERE id=1)),
          last_played=coalesce((SELECT max(e.started) FROM tracks t JOIN listening_events e ON e.track_id=t.id
            WHERE t.album_key=albums.id AND t.missing=0 AND e.history_epoch=(SELECT epoch FROM history_state WHERE id=1)),0)
          WHERE {predicate};
        """ + "\n";
}
