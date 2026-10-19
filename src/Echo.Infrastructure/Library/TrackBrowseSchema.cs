using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class TrackBrowseSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Duplicate lookups use the same normalized identity as the query, then a narrow duration range.
        command.CommandText = """
            ALTER TABLE tracks ADD COLUMN bitrate INTEGER;
            ALTER TABLE tracks ADD COLUMN bpm REAL;
            CREATE INDEX tracks_bitrate_page ON tracks(bitrate,id) WHERE missing=0;
            CREATE INDEX tracks_bpm_page ON tracks(bpm,id) WHERE missing=0;
            CREATE INDEX tracks_duplicate_identity ON tracks(echo_fold(title),echo_fold(artist),duration,id) WHERE missing=0;
            PRAGMA user_version=22;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
