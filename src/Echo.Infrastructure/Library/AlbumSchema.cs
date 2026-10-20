using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class AlbumSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE tracks ADD COLUMN album_credit TEXT GENERATED ALWAYS AS
              (trim(coalesce(nullif(trim(album_artist),''),artist))) VIRTUAL;
            ALTER TABLE tracks ADD COLUMN album_key TEXT GENERATED ALWAYS AS
              (printf('%d:%s%d:%s',length(trim(album)),trim(album),length(album_credit),album_credit)) VIRTUAL;
            CREATE INDEX tracks_album ON tracks(album_key,disc_no,track_no,sort_key,id) WHERE missing=0;
            CREATE INDEX tracks_album_year ON tracks(album_key,year DESC) WHERE missing=0;
            CREATE TABLE albums(
              id TEXT PRIMARY KEY, title TEXT NOT NULL, artist TEXT NOT NULL,
              sort_key TEXT NOT NULL, artist_key TEXT NOT NULL,
              track_count INTEGER NOT NULL, duration REAL NOT NULL,
              lossless_count INTEGER NOT NULL, year INTEGER, added INTEGER NOT NULL);
            CREATE INDEX albums_title ON albums(sort_key,id);
            CREATE INDEX albums_artist ON albums(artist_key,id);
            CREATE INDEX albums_year ON albums(year,id);
            CREATE INDEX albums_recent ON albums(added,id);
            CREATE INDEX albums_duration ON albums(duration,id);
            CREATE INDEX albums_count ON albums(track_count,id);
            INSERT INTO albums
              SELECT album_key,coalesce(nullif(trim(album),''),'未知专辑'),
                coalesce(nullif(album_credit,''),'未知艺术家'),
                upper(coalesce(nullif(trim(album),''),'未知专辑')),
                upper(coalesce(nullif(album_credit,''),'未知艺术家')),
                count(*),sum(duration),sum(lower(codec) IN ('flac','alac','wav','aiff','ape','wv')),
                max(year),max(updated_at)
              FROM tracks WHERE missing=0 GROUP BY album_key;
            CREATE VIRTUAL TABLE albums_fts USING fts5(title,artist,content='albums',content_rowid='rowid',
              tokenize='unicode61 remove_diacritics 2',prefix='2 3 4');
            INSERT INTO albums_fts(albums_fts) VALUES('rebuild');
            CREATE TRIGGER albums_fts_insert AFTER INSERT ON albums BEGIN
              INSERT INTO albums_fts(rowid,title,artist) VALUES(new.rowid,new.title,new.artist);
            END;
            CREATE TRIGGER albums_fts_delete AFTER DELETE ON albums BEGIN
              INSERT INTO albums_fts(albums_fts,rowid,title,artist) VALUES('delete',old.rowid,old.title,old.artist);
            END;
            """ + TrackTriggers + " PRAGMA user_version=2;";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string Add(string row, bool deferred) => $"""
        INSERT INTO albums(id,title,artist,sort_key,artist_key,track_count,duration,lossless_count,year,added)
          SELECT {row}.album_key,coalesce(nullif(trim({row}.album),''),'未知专辑'),
            coalesce(nullif({row}.album_credit,''),'未知艺术家'),
            upper(coalesce(nullif(trim({row}.album),''),'未知专辑')),
            upper(coalesce(nullif({row}.album_credit,''),'未知艺术家')),
            1,{row}.duration,lower({row}.codec) IN ('flac','alac','wav','aiff','ape','wv'),{row}.year,{row}.updated_at
          WHERE {row}.missing=0
        ON CONFLICT(id) DO UPDATE SET track_count=track_count+1,duration=duration+excluded.duration,
          lossless_count=lossless_count+excluded.lossless_count,
          year=(SELECT year FROM tracks WHERE album_key={row}.album_key AND missing=0 ORDER BY year DESC LIMIT 1),
          added=max(added,excluded.added);
        """ + Refresh(row, deferred);

    private static string Remove(string row, bool deferred) => $"""
        UPDATE albums SET track_count=track_count-1,duration=max(0,duration-{row}.duration),
          lossless_count=lossless_count-(lower({row}.codec) IN ('flac','alac','wav','aiff','ape','wv')),
          year=(SELECT year FROM tracks WHERE album_key={row}.album_key AND missing=0 ORDER BY year DESC LIMIT 1)
          WHERE id={row}.album_key AND {row}.missing=0;
        DELETE FROM albums WHERE id={row}.album_key AND track_count=0;
        """ + Refresh(row, deferred);

    private static string Refresh(string row, bool deferred) =>
        (deferred ? $"INSERT INTO scan_album_dirty SELECT {row}.album_key WHERE echo_scan_deferred() ON CONFLICT(album_id) DO NOTHING;\n" : "") +
        AlbumDisplay.Refresh($"id={row}.album_key" + (deferred ? " AND NOT echo_scan_deferred()" : ""));

    internal static string TrackTriggers => BuildTriggers(false);
    internal static string DeferredTrackTriggers => BuildTriggers(true);
    private static string BuildTriggers(bool deferred) => """
        CREATE TRIGGER tracks_album_insert AFTER INSERT ON tracks WHEN new.missing=0 BEGIN
        """ + "\n" + Add("new", deferred) + " END; " +
        "CREATE TRIGGER tracks_album_delete AFTER DELETE ON tracks WHEN old.missing=0 BEGIN " + Remove("old", deferred) + " END; " +
        """
        CREATE TRIGGER tracks_album_update AFTER UPDATE OF album,album_artist,artist,duration,codec,year,missing ON tracks
        WHEN old.album_key IS NOT new.album_key OR old.album IS NOT new.album OR old.album_artist IS NOT new.album_artist
             OR old.artist IS NOT new.artist OR old.duration IS NOT new.duration OR old.codec IS NOT new.codec
             OR old.year IS NOT new.year OR old.missing IS NOT new.missing BEGIN
        """ + "\n" + Remove("old", deferred) + Add("new", deferred) + " END;";
}
