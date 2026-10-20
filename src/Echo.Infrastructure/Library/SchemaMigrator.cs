using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class SchemaMigrator
{
    private const long ApplicationId = 0x45434841;
    public static void Initialize(SqliteConnection connection)
    {
        using var probe = connection.CreateCommand();
        probe.CommandText = "PRAGMA application_id";
        var id = (long)probe.ExecuteScalar()!;
        probe.CommandText = "PRAGMA user_version";
        var version = (long)probe.ExecuteScalar()!;
        probe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'";
        var objects = (long)probe.ExecuteScalar()!;
        if ((id != ApplicationId && (id != 0 || objects != 0)) || version > 28)
            throw new InvalidOperationException("This is not a supported ECHO Avalonia database. Legacy databases require an explicit importer.");
        if (version is > 0 and < 4) ArtistSchema.Backup(connection);
        if (version == 0)
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = Schema;
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        if (version < 2) AlbumSchema.Migrate(connection);
        if (version < 3)
        {
            using var transaction = connection.BeginTransaction();
            using var state = connection.CreateCommand();
            state.Transaction = transaction;
            state.CommandText = """
                CREATE TABLE album_user_state(album_id TEXT PRIMARY KEY, favorite INTEGER NOT NULL DEFAULT 0,
                  rating INTEGER NOT NULL DEFAULT 0 CHECK(rating BETWEEN 0 AND 5));
                PRAGMA user_version=3;
                """;
            state.ExecuteNonQuery(); transaction.Commit();
        }
        if (version < 4) ArtistSchema.Migrate(connection);
        if (version < 5) VirtualSourceSchema.Migrate(connection);
        if (version < 6)
        {
            if (version > 0) AlbumMetadataSchema.Backup(connection);
            AlbumMetadataSchema.Migrate(connection);
        }
        if (version < 7) VirtualAudioIdentitySchema.Migrate(connection);
        if (version < 8) Echo.Infrastructure.Search.LibrarySearchSchema.Migrate(connection);
        if (version < 9) FolderSchema.Migrate(connection);
        if (version < 10) ListeningSchema.Migrate(connection);
        if (version < 11)
        {
            if (version > 0) AlbumMetadataSchema.Backup(connection, 11);
            PlaylistSchema.Migrate(connection);
        }
        if (version < 12) AlbumBrowseSchema.Migrate(connection);
        if (version < 13)
        {
            if (version > 0) AlbumMetadataSchema.Backup(connection, 13);
            GenreSchema.Migrate(connection);
        }
        if (version < 14)
        {
            if (version > 0) AlbumMetadataSchema.Backup(connection, 14);
            HistorySchema.Migrate(connection);
        }
        if (version < 15) AlbumListeningSchema.Migrate(connection);
        if (version < 16)
        {
            if (version > 0) AlbumMetadataSchema.Backup(connection, 16);
            HistoryQualitySchema.Migrate(connection);
        }
        if (version < 17)
        {
            if (version > 0) AlbumMetadataSchema.Backup(connection, 17);
            FolderWorkbenchSchema.Migrate(connection);
        }
        if (version < 18)
        {
            if (version > 0) AlbumMetadataSchema.Backup(connection, 18);
            LargeLibrarySchema.Migrate(connection);
        }
        if (version < 21)
        {
            if (version > 0) AlbumMetadataSchema.Backup(connection, 21);
            AlbumGroupingSchema.Migrate(connection);
        }
        if (version < 22) TrackBrowseSchema.Migrate(connection);
        if (version < 23)
        {
            if (version > 0) AlbumMetadataSchema.Backup(connection, 23);
            ScanIndexSchema.Migrate(connection);
        }
        if (version < 24)
        {
            if (version == 23) AlbumMetadataSchema.Backup(connection, 24);
            ScanIndexSchema.RepairConflictHandling(connection);
        }
        if (version < 27)
        {
            if (version > 0) AlbumMetadataSchema.Backup(connection, 27);
            ArtistGroupingSchema.Migrate(connection, includeAlbumArtists: version < 25);
        }
        if (version < 28)
        {
            if (version == 27) AlbumMetadataSchema.Backup(connection, 28);
            ArtistRoleSchema.Migrate(connection);
        }
        probe.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
        probe.ExecuteNonQuery();
        probe.CommandText = "UPDATE scan_jobs SET state='Interrupted', error_code='process_interrupted', finished_at=unixepoch() WHERE state='Running'; DELETE FROM scan_seen; UPDATE listening_events SET ended=updated WHERE ended IS NULL;";
        probe.ExecuteNonQuery();
    }

    private const string Schema = """
        PRAGMA application_id=1162037313;
        PRAGMA user_version=1;
        CREATE TABLE library_state(id INTEGER PRIMARY KEY CHECK(id=1), revision INTEGER NOT NULL);
        INSERT INTO library_state VALUES(1,0);
        CREATE TABLE tracks(
          id TEXT PRIMARY KEY, path TEXT NOT NULL, path_key TEXT NOT NULL UNIQUE,
          size_bytes INTEGER NOT NULL, modified_ticks INTEGER NOT NULL, parser_version INTEGER NOT NULL,
          title TEXT NOT NULL, sort_key TEXT NOT NULL, artist TEXT NOT NULL, album TEXT NOT NULL,
          album_artist TEXT NOT NULL, duration REAL NOT NULL, codec TEXT NOT NULL,
          sample_rate INTEGER NOT NULL, bit_depth INTEGER NOT NULL,
          track_no INTEGER, disc_no INTEGER, year INTEGER, genre TEXT NOT NULL,
          missing INTEGER NOT NULL DEFAULT 0, favorite INTEGER NOT NULL DEFAULT 0,
          updated_at INTEGER NOT NULL);
        CREATE INDEX tracks_page ON tracks(sort_key,id) WHERE missing=0;
        CREATE TABLE scan_jobs(
          id TEXT PRIMARY KEY, root_prefix TEXT NOT NULL, state TEXT NOT NULL,
          discovered INTEGER NOT NULL DEFAULT 0, processed INTEGER NOT NULL DEFAULT 0,
          inserted INTEGER NOT NULL DEFAULT 0, updated INTEGER NOT NULL DEFAULT 0,
          unchanged INTEGER NOT NULL DEFAULT 0, failed INTEGER NOT NULL DEFAULT 0,
          marked_missing INTEGER NOT NULL DEFAULT 0, error_code TEXT,
          track_presence INTEGER NOT NULL DEFAULT 0,
          last_batch INTEGER NOT NULL DEFAULT 0, last_fingerprint TEXT,
          last_inserted INTEGER NOT NULL DEFAULT 0, last_updated INTEGER NOT NULL DEFAULT 0,
          last_unchanged INTEGER NOT NULL DEFAULT 0, last_failed INTEGER NOT NULL DEFAULT 0,
          last_revision INTEGER NOT NULL DEFAULT 0,
          started_at INTEGER NOT NULL, finished_at INTEGER);
        CREATE UNIQUE INDEX one_active_scan ON scan_jobs(state) WHERE state='Running';
        CREATE TABLE scan_seen(
          scan_id TEXT NOT NULL REFERENCES scan_jobs(id), path_key TEXT NOT NULL,
          PRIMARY KEY(scan_id,path_key)) WITHOUT ROWID;
        CREATE TABLE scan_errors(
          scan_id TEXT NOT NULL REFERENCES scan_jobs(id), ordinal INTEGER NOT NULL,
          path TEXT NOT NULL, code TEXT NOT NULL, PRIMARY KEY(scan_id,ordinal)) WITHOUT ROWID;
        CREATE VIRTUAL TABLE tracks_fts USING fts5(
          title,artist,album,content='tracks',content_rowid='rowid',
          tokenize='unicode61 remove_diacritics 2',prefix='2 3 4');
        CREATE TRIGGER tracks_fts_insert AFTER INSERT ON tracks BEGIN
          INSERT INTO tracks_fts(rowid,title,artist,album) VALUES(new.rowid,new.title,new.artist,new.album);
        END;
        CREATE TRIGGER tracks_fts_delete AFTER DELETE ON tracks BEGIN
          INSERT INTO tracks_fts(tracks_fts,rowid,title,artist,album)
          VALUES('delete',old.rowid,old.title,old.artist,old.album);
        END;
        CREATE TRIGGER tracks_fts_update AFTER UPDATE OF title,artist,album ON tracks
        WHEN old.title IS NOT new.title OR old.artist IS NOT new.artist OR old.album IS NOT new.album BEGIN
          INSERT INTO tracks_fts(tracks_fts,rowid,title,artist,album)
          VALUES('delete',old.rowid,old.title,old.artist,old.album);
          INSERT INTO tracks_fts(rowid,title,artist,album) VALUES(new.rowid,new.title,new.artist,new.album);
        END;
        """;
}
