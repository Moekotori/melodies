using Echo.Contracts.Library;
using Echo.Infrastructure.Files;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;
public sealed partial class LibraryDatabase
{
    private static void ReconcileVirtualAudio(SqliteConnection connection, SqliteTransaction transaction,
        ScanEntry entry, ArtistIndexWriter artists, CancellationToken cancellation)
    {
        var source = entry.File.Source;
        if (source?.Kind == "osu") return;
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        if (source?.Kind == "cue")
        {
            var key = LibraryPath.Key(source.AudioPath);
            command.CommandText = "UPDATE tracks SET missing=1 WHERE path_key=$path AND missing=0";
            command.Parameters.AddWithValue("$path", key);
            if (command.ExecuteNonQuery() > 0) artists.Update(key, cancellation);
        }
        else
        {
            command.CommandText = "UPDATE tracks SET missing=1 WHERE physical_path=$path AND source_kind='cue' AND missing=0 RETURNING path_key";
            command.Parameters.AddWithValue("$path", entry.File.Path);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                // UPDATE RETURNING is consumed before other statements use the same connection.
            }
            // Membership cleanup is bounded and SQL-driven, even for old virtual rows.
            using var cleanup = connection.CreateCommand(); cleanup.Transaction = transaction;
            cleanup.CommandText = "DELETE FROM artist_tracks WHERE track_id IN(SELECT id FROM tracks WHERE physical_path=$path AND source_kind='cue' AND missing=1)";
            cleanup.Parameters.AddWithValue("$path", entry.File.Path);
            cleanup.ExecuteNonQuery();
        }
    }
}
