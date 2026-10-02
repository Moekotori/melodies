using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class ArtistSchema
{
    public static void Backup(SqliteConnection connection)
    {
        var path = Path.GetFullPath(connection.DataSource) + ".before-v4-" + Guid.NewGuid().ToString("N") + ".sqlite";
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        backup.Open(); connection.BackupDatabase(backup);
    }
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = Schema;
        command.ExecuteNonQuery();
        using var index = new ArtistIndexWriter(connection, transaction);
        long after = 0;
        while (true)
        {
            command.CommandText = "SELECT rowid,id,artist,album_credit,album_key,duration,updated_at FROM tracks WHERE rowid>$after AND missing=0 ORDER BY rowid LIMIT 200";
            command.Parameters.Clear(); command.Parameters.AddWithValue("$after", after);
            var rows = new List<(long Row, string Id, string Credit, string AlbumCredit, string Album, double Duration, long Added)>(200);
            using (var reader = command.ExecuteReader())
                while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDouble(5), reader.GetInt64(6)));
            if (rows.Count == 0) break;
            foreach (var row in rows) index.Replace(row.Id, row.Credit, row.AlbumCredit, row.Album, row.Duration, row.Added, CancellationToken.None);
            after = rows[^1].Row;
        }
        command.CommandText = "PRAGMA user_version=4"; command.ExecuteNonQuery(); transaction.Commit();
    }
    private const string Schema = """
        CREATE TABLE artists(grouping INTEGER NOT NULL,id TEXT NOT NULL,name TEXT NOT NULL,sort_key TEXT NOT NULL,
          track_count INTEGER NOT NULL,album_count INTEGER NOT NULL,duration REAL NOT NULL,added INTEGER NOT NULL,
          PRIMARY KEY(grouping,id));
        CREATE INDEX artists_name ON artists(grouping,sort_key,id);
        CREATE INDEX artists_tracks ON artists(grouping,track_count,id);
        CREATE INDEX artists_albums ON artists(grouping,album_count,id);
        CREATE INDEX artists_recent ON artists(grouping,added,id);
        CREATE TABLE artist_tracks(grouping INTEGER NOT NULL,artist_id TEXT NOT NULL,name TEXT NOT NULL,
          track_id TEXT NOT NULL,album_id TEXT NOT NULL,duration REAL NOT NULL,added INTEGER NOT NULL,
          PRIMARY KEY(grouping,artist_id,track_id)) WITHOUT ROWID;
        CREATE INDEX artist_tracks_owner ON artist_tracks(track_id);
        CREATE INDEX artist_tracks_album ON artist_tracks(grouping,artist_id,album_id,track_id);
        CREATE TABLE artist_albums(grouping INTEGER NOT NULL,artist_id TEXT NOT NULL,album_id TEXT NOT NULL,
          track_count INTEGER NOT NULL,PRIMARY KEY(grouping,artist_id,album_id)) WITHOUT ROWID;
        CREATE VIRTUAL TABLE artists_fts USING fts5(name,content='artists',content_rowid='rowid',tokenize='unicode61 remove_diacritics 2',prefix='2 3 4');
        CREATE TRIGGER artists_fts_insert AFTER INSERT ON artists BEGIN
          INSERT INTO artists_fts(rowid,name) VALUES(new.rowid,new.name);
        END;
        CREATE TRIGGER artists_fts_delete AFTER DELETE ON artists BEGIN
          INSERT INTO artists_fts(artists_fts,rowid,name) VALUES('delete',old.rowid,old.name);
        END;
        CREATE TRIGGER artist_track_insert AFTER INSERT ON artist_tracks BEGIN
          INSERT INTO artists VALUES(new.grouping,new.artist_id,new.name,upper(new.name),1,0,new.duration,new.added)
          ON CONFLICT(grouping,id) DO UPDATE SET track_count=track_count+1,duration=duration+excluded.duration,added=max(added,excluded.added);
          INSERT INTO artist_albums VALUES(new.grouping,new.artist_id,new.album_id,1)
          ON CONFLICT(grouping,artist_id,album_id) DO UPDATE SET track_count=track_count+1;
          UPDATE artists SET album_count=album_count+1 WHERE grouping=new.grouping AND id=new.artist_id AND
            (SELECT track_count FROM artist_albums WHERE grouping=new.grouping AND artist_id=new.artist_id AND album_id=new.album_id)=1;
        END;
        CREATE TRIGGER artist_track_delete AFTER DELETE ON artist_tracks BEGIN
          UPDATE artists SET track_count=track_count-1,duration=max(0,duration-old.duration),album_count=album_count-
            (SELECT track_count=1 FROM artist_albums WHERE grouping=old.grouping AND artist_id=old.artist_id AND album_id=old.album_id)
            WHERE grouping=old.grouping AND id=old.artist_id;
          UPDATE artist_albums SET track_count=track_count-1 WHERE grouping=old.grouping AND artist_id=old.artist_id AND album_id=old.album_id;
          DELETE FROM artist_albums WHERE grouping=old.grouping AND artist_id=old.artist_id AND album_id=old.album_id AND track_count=0;
          DELETE FROM artists WHERE grouping=old.grouping AND id=old.artist_id AND track_count=0;
        END;
        CREATE TRIGGER tracks_artist_missing AFTER UPDATE OF missing ON tracks WHEN new.missing=1 AND old.missing=0 BEGIN
          DELETE FROM artist_tracks WHERE track_id=new.id;
        END;
        CREATE TRIGGER tracks_artist_delete AFTER DELETE ON tracks BEGIN DELETE FROM artist_tracks WHERE track_id=old.id; END;
        """;
}
