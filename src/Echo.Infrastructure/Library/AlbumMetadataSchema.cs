using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class AlbumMetadataSchema
{
    public static void Backup(SqliteConnection connection)
    {
        // SQLite's backup API captures WAL consistently. Never copy only the main database file.
        var path = Path.GetFullPath(connection.DataSource) + ".before-v6-" + Guid.NewGuid().ToString("N") + ".sqlite";
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        backup.Open();
        connection.BackupDatabase(backup);
    }

    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS album_identity(
              group_key TEXT PRIMARY KEY, stable_id TEXT NOT NULL UNIQUE,
              file_title TEXT NOT NULL, file_artist TEXT NOT NULL,
              display_title TEXT NOT NULL, display_artist TEXT NOT NULL,
              title_key TEXT NOT NULL, artist_key TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS album_identity_title ON album_identity(title_key,stable_id);
            CREATE INDEX IF NOT EXISTS album_identity_artist ON album_identity(artist_key,stable_id);
            CREATE TABLE IF NOT EXISTS album_metadata_values(
              album_id TEXT NOT NULL, field INTEGER NOT NULL CHECK(field IN (0,1)),
              source INTEGER NOT NULL CHECK(source IN (1,2)), value TEXT NOT NULL CHECK(length(value)<=1024),
              PRIMARY KEY(album_id,field,source));
            INSERT OR IGNORE INTO album_identity
              SELECT id,lower(hex(randomblob(16))),title,artist,title,artist,sort_key,artist_key FROM albums;
            CREATE VIRTUAL TABLE IF NOT EXISTS album_display_fts USING fts5(title,artist,
              tokenize='unicode61 remove_diacritics 2',prefix='2 3 4');
            DELETE FROM album_display_fts;
            INSERT INTO album_display_fts(rowid,title,artist)
              SELECT a.rowid,i.display_title,i.display_artist FROM albums a JOIN album_identity i ON i.group_key=a.id;
            CREATE TRIGGER IF NOT EXISTS album_identity_insert AFTER INSERT ON albums BEGIN
              INSERT INTO album_identity VALUES(new.id,lower(hex(randomblob(16))),new.title,new.artist,
                new.title,new.artist,new.sort_key,new.artist_key)
                ON CONFLICT(group_key) DO UPDATE SET file_title=excluded.file_title,file_artist=excluded.file_artist;
            """ + Refresh("group_key=new.id") + """
              INSERT INTO album_display_fts(rowid,title,artist)
                SELECT new.rowid,display_title,display_artist FROM album_identity WHERE group_key=new.id;
            END;
            CREATE TRIGGER IF NOT EXISTS album_identity_file_update AFTER UPDATE OF title,artist ON albums BEGIN
              UPDATE album_identity SET file_title=new.title,file_artist=new.artist WHERE group_key=new.id;
            """ + Refresh("group_key=new.id") + """
            END;
            CREATE TRIGGER IF NOT EXISTS album_identity_delete AFTER DELETE ON albums BEGIN
              DELETE FROM album_display_fts WHERE rowid=old.rowid;
            END;
            CREATE TRIGGER IF NOT EXISTS album_identity_display_update AFTER UPDATE OF display_title,display_artist ON album_identity BEGIN
              UPDATE album_display_fts SET title=new.display_title,artist=new.display_artist
                WHERE rowid=(SELECT rowid FROM albums WHERE id=new.group_key);
            END;
            CREATE TRIGGER IF NOT EXISTS album_metadata_insert AFTER INSERT ON album_metadata_values BEGIN
            """ + Refresh("stable_id=new.album_id") + """
            END;
            CREATE TRIGGER IF NOT EXISTS album_metadata_update AFTER UPDATE OF value ON album_metadata_values BEGIN
            """ + Refresh("stable_id=new.album_id") + """
            END;
            CREATE TRIGGER IF NOT EXISTS album_metadata_delete AFTER DELETE ON album_metadata_values BEGIN
            """ + Refresh("stable_id=old.album_id") + " END; PRAGMA user_version=6;";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string Refresh(string predicate)
    {
        const string title = "coalesce((SELECT value FROM album_metadata_values WHERE album_id=stable_id AND field=0 ORDER BY source DESC LIMIT 1),file_title)";
        const string artist = "coalesce((SELECT value FROM album_metadata_values WHERE album_id=stable_id AND field=1 ORDER BY source DESC LIMIT 1),file_artist)";
        return $"\nUPDATE album_identity SET display_title={title},display_artist={artist},title_key=upper({title}),artist_key=upper({artist}) WHERE {predicate};\n";
    }
}
