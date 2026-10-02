using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// First import must not recount the growing album's complete history for every new track.
internal static class LargeLibrarySchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            DROP TRIGGER tracks_browse_insert;
            CREATE TRIGGER tracks_browse_insert AFTER INSERT ON tracks WHEN new.missing=0 BEGIN
              UPDATE albums SET
                play_count=play_count+(SELECT count(*) FROM listening_events WHERE track_id=new.id
                  AND counted=1 AND ended IS NOT NULL AND history_epoch=(SELECT epoch FROM history_state WHERE id=1)),
                last_played=max(last_played,coalesce((SELECT max(started) FROM listening_events WHERE track_id=new.id
                  AND history_epoch=(SELECT epoch FROM history_state WHERE id=1)),0))
                WHERE id=new.album_key;
            """ + AlbumBrowseSchema.RefreshDates("id=new.album_key") + """
            END;
            PRAGMA user_version=18;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }
}
