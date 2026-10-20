using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Rebuild derived credits in bounded pages; media and personal state stay intact.
internal static class ArtistGroupingSchema
{
    public static void Migrate(SqliteConnection connection, bool includeAlbumArtists)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        using var artists = new ArtistIndexWriter(connection, transaction);
        command.Parameters.AddWithValue("$after", 0L);
        while (true)
        {
            command.CommandText = """
                SELECT t.rowid,t.id,t.artist,coalesce(nullif(trim(t.album_artist),''),a.artist,t.album_credit),
                  t.album_key,t.duration,t.updated_at
                FROM tracks t LEFT JOIN albums a ON a.id=t.album_key
                WHERE t.rowid>$after AND t.missing=0 ORDER BY t.rowid LIMIT 200
                """;
            var rows = new List<(long Row, string Id, string Credit, string AlbumCredit, string Album, double Duration, long Added)>(200);
            using (var reader = command.ExecuteReader())
                while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetDouble(5), reader.GetInt64(6)));
            if (rows.Count == 0) break;
            foreach (var row in rows)
            {
                artists.ReplaceGrouping(0, row.Id, row.Credit, row.Album, row.Duration, row.Added);
                if (includeAlbumArtists || row.AlbumCredit.Contains('\0'))
                    artists.ReplaceGrouping(1, row.Id, row.AlbumCredit, row.Album, row.Duration, row.Added);
            }
            command.Parameters[0].Value = rows[^1].Row;
        }
        command.CommandText = """
            UPDATE library_state SET revision=revision+1
              WHERE id=1 AND EXISTS(SELECT 1 FROM artist_tracks WHERE grouping=0);
            PRAGMA user_version=27;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }
}
