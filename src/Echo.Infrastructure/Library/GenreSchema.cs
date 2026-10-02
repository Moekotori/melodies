using System.Text;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Derived facts share the scan transaction and writer. No second library owner.
internal static class GenreSchema
{
    public static string Key(string value)
    {
        var key = value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        return key.Length == 0 ? "__unclassified__" : key;
    }
    public static string Name(string value) => value.Normalize(NormalizationForm.FormKC).Trim();
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = Schema; command.ExecuteNonQuery();
        // SQL streaming backfill: managed memory never holds the library.
        command.CommandText = "INSERT INTO genre_tracks SELECT echo_genre_key(genre),id,echo_genre_name(genre),album_key,duration,updated_at FROM tracks WHERE missing=0; PRAGMA user_version=13;";
        command.ExecuteNonQuery(); transaction.Commit();
    }
    private const string Schema = """
        CREATE TABLE genres(id TEXT PRIMARY KEY,name TEXT NOT NULL,track_count INTEGER NOT NULL,
          album_count INTEGER NOT NULL,duration REAL NOT NULL);
        CREATE INDEX genres_tracks ON genres(track_count,id);
        CREATE INDEX genres_albums ON genres(album_count,id);
        CREATE TABLE genre_tracks(genre_id TEXT NOT NULL,track_id TEXT NOT NULL,name TEXT NOT NULL,
          album_id TEXT NOT NULL,duration REAL NOT NULL,added INTEGER NOT NULL,
          PRIMARY KEY(genre_id,track_id)) WITHOUT ROWID;
        CREATE INDEX genre_tracks_owner ON genre_tracks(track_id);
        CREATE INDEX genre_tracks_names ON genre_tracks(genre_id,name);
        CREATE TABLE genre_albums(genre_id TEXT NOT NULL,album_id TEXT NOT NULL,track_count INTEGER NOT NULL,
          PRIMARY KEY(genre_id,album_id)) WITHOUT ROWID;
        CREATE TRIGGER genre_track_insert AFTER INSERT ON genre_tracks BEGIN
          INSERT INTO genres VALUES(new.genre_id,new.name,1,0,new.duration)
          ON CONFLICT(id) DO UPDATE SET track_count=track_count+1,duration=duration+excluded.duration;
          INSERT INTO genre_albums VALUES(new.genre_id,new.album_id,1)
          ON CONFLICT(genre_id,album_id) DO UPDATE SET track_count=track_count+1;
          UPDATE genres SET album_count=album_count+1 WHERE id=new.genre_id AND
            (SELECT track_count FROM genre_albums WHERE genre_id=new.genre_id AND album_id=new.album_id)=1;
        END;
        CREATE TRIGGER genre_track_delete AFTER DELETE ON genre_tracks BEGIN
          UPDATE genres SET track_count=track_count-1,duration=max(0,duration-old.duration),album_count=album_count-
            (SELECT track_count=1 FROM genre_albums WHERE genre_id=old.genre_id AND album_id=old.album_id)
            WHERE id=old.genre_id;
          UPDATE genre_albums SET track_count=track_count-1 WHERE genre_id=old.genre_id AND album_id=old.album_id;
          DELETE FROM genre_albums WHERE genre_id=old.genre_id AND album_id=old.album_id AND track_count=0;
          DELETE FROM genres WHERE id=old.genre_id AND track_count=0;
          UPDATE genres SET name=(SELECT name FROM genre_tracks WHERE genre_id=old.genre_id ORDER BY track_id LIMIT 1)
            WHERE id=old.genre_id AND name=old.name;
        END;
        CREATE TRIGGER tracks_genre_insert AFTER INSERT ON tracks WHEN new.missing=0 BEGIN
          INSERT INTO genre_tracks VALUES(echo_genre_key(new.genre),new.id,echo_genre_name(new.genre),new.album_key,new.duration,new.updated_at);
        END;
        CREATE TRIGGER tracks_genre_update AFTER UPDATE OF genre,album,album_artist,artist,duration,missing ON tracks
        WHEN old.genre IS NOT new.genre OR old.album_key IS NOT new.album_key OR old.duration IS NOT new.duration OR old.missing IS NOT new.missing BEGIN
          DELETE FROM genre_tracks WHERE track_id=old.id;
          INSERT INTO genre_tracks SELECT echo_genre_key(new.genre),new.id,echo_genre_name(new.genre),new.album_key,new.duration,new.updated_at WHERE new.missing=0;
        END;
        CREATE TRIGGER tracks_genre_delete AFTER DELETE ON tracks BEGIN DELETE FROM genre_tracks WHERE track_id=old.id; END;
        """;
}
