using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Persist browse facts in the writer transaction; reads never aggregate the listening history.
internal static class AlbumBrowseSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE albums ADD COLUMN created INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE albums ADD COLUMN modified INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE albums ADD COLUMN play_count INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE albums ADD COLUMN last_played INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE albums ADD COLUMN shuffle_key TEXT NOT NULL DEFAULT '';
            CREATE INDEX tracks_album_created ON tracks(album_key,created_at) WHERE missing=0;
            CREATE INDEX tracks_album_modified ON tracks(album_key,modified_ticks) WHERE missing=0;
            UPDATE albums SET shuffle_key=hex(randomblob(8));
            """ + Refresh("1=1") + """
            CREATE INDEX albums_created ON albums(created,id);
            CREATE INDEX albums_modified ON albums(modified,id);
            CREATE INDEX albums_play_count ON albums(play_count,id);
            CREATE INDEX albums_last_played ON albums(last_played,id);
            CREATE INDEX albums_shuffle ON albums(shuffle_key,id);
            CREATE INDEX tracks_album_page ON tracks(album COLLATE NOCASE,id) WHERE missing=0;
            CREATE INDEX tracks_number_page ON tracks(disc_no,track_no,id) WHERE missing=0;
            CREATE INDEX tracks_favorite_page ON tracks(sort_key,id) WHERE missing=0 AND favorite=1;
            CREATE TRIGGER albums_browse_insert AFTER INSERT ON albums BEGIN
              UPDATE albums SET shuffle_key=hex(randomblob(8)) WHERE id=new.id;
            """ + Refresh("id=new.id") + """
            END;
            CREATE TRIGGER tracks_browse_insert AFTER INSERT ON tracks WHEN new.missing=0 BEGIN
            """ + Refresh("id=new.album_key") + """
            END;
            CREATE TRIGGER tracks_browse_delete AFTER DELETE ON tracks WHEN old.missing=0 BEGIN
            """ + Refresh("id=old.album_key") + """
            END;
            CREATE TRIGGER tracks_browse_update AFTER UPDATE OF album,album_artist,artist,missing ON tracks
            WHEN old.album_key IS NOT new.album_key OR old.missing IS NOT new.missing BEGIN
            """ + Refresh("id IN(old.album_key,new.album_key)") + """
            END;
            CREATE TRIGGER tracks_browse_dates AFTER UPDATE OF created_at,modified_ticks ON tracks
            WHEN old.created_at IS NOT new.created_at OR old.modified_ticks IS NOT new.modified_ticks BEGIN
            """ + RefreshDates("id=new.album_key") + """
            END;
            CREATE TRIGGER listening_browse_insert AFTER INSERT ON listening_events BEGIN
              UPDATE albums SET play_count=play_count+1,last_played=max(last_played,new.started)
                WHERE id=(SELECT album_key FROM tracks WHERE id=new.track_id AND missing=0);
              UPDATE library_state SET revision=revision+1 WHERE id=1;
            END;
            CREATE TRIGGER listening_browse_delete AFTER DELETE ON listening_events BEGIN
            """ + Refresh("id=(SELECT album_key FROM tracks WHERE id=old.track_id)") + """
              UPDATE library_state SET revision=revision+1 WHERE id=1;
            END;
            CREATE TRIGGER listening_browse_update AFTER UPDATE OF track_id,started ON listening_events
            WHEN old.track_id IS NOT new.track_id OR old.started IS NOT new.started BEGIN
            """ + Refresh("id IN(SELECT album_key FROM tracks WHERE id IN(old.track_id,new.track_id))") + """
              UPDATE library_state SET revision=revision+1 WHERE id=1;
            END;
            PRAGMA user_version=12;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }

    private static string Refresh(string predicate) => "\n" + $"""
        UPDATE albums SET
          created=coalesce((SELECT created_at FROM tracks WHERE album_key=albums.id AND missing=0 ORDER BY created_at LIMIT 1),0),
          modified=coalesce((SELECT modified_ticks FROM tracks WHERE album_key=albums.id AND missing=0 ORDER BY modified_ticks DESC LIMIT 1),0),
          play_count=(SELECT count(*) FROM tracks t JOIN listening_events e ON e.track_id=t.id WHERE t.album_key=albums.id AND t.missing=0),
          last_played=coalesce((SELECT max(e.started) FROM tracks t JOIN listening_events e ON e.track_id=t.id WHERE t.album_key=albums.id AND t.missing=0),0)
          WHERE {predicate};
        """ + "\n";

    internal static string RefreshDates(string predicate) => "\n" + $"""
        UPDATE albums SET
          created=coalesce((SELECT created_at FROM tracks WHERE album_key=albums.id AND missing=0 ORDER BY created_at LIMIT 1),0),
          modified=coalesce((SELECT modified_ticks FROM tracks WHERE album_key=albums.id AND missing=0 ORDER BY modified_ticks DESC LIMIT 1),0)
          WHERE {predicate};
        """ + "\n";
}
