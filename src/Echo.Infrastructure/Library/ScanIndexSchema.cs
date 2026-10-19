using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class ScanIndexSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE scan_album_dirty(album_id TEXT PRIMARY KEY) WITHOUT ROWID;
            CREATE INDEX artist_tracks_album_members ON artist_tracks(album_id,grouping,artist_id,name,track_id);
            CREATE INDEX tracks_album_members ON tracks(album_key,id) WHERE missing=0;
            DROP TRIGGER tracks_album_insert;
            DROP TRIGGER tracks_album_delete;
            DROP TRIGGER tracks_album_update;
            """ + AlbumSchema.DeferredTrackTriggers + " PRAGMA user_version=23;";
        command.ExecuteNonQuery(); transaction.Commit();
    }
}
