using Echo.Contracts.Library;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task<ArtistImportSettings> RebuildArtistCreditsAsync(ArtistImportSettings settings, IProgress<ArtistImportProgress> progress, CancellationToken cancellation)
    {
        _ = new ArtistCreditPolicy(settings.Delimiters);
        var requested = settings.Delimiters.ToArray();
        return _writer.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction();
            var current = ArtistCreditPolicy.Read(connection, transaction);
            if (settings.Revision != current.Revision) throw new ArtistImportChangedException();
            if (current.Delimiters.SequenceEqual(requested)) return current;
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "DELETE FROM artist_delimiters"; command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO artist_delimiters VALUES($ordinal,$value)";
            command.Parameters.AddWithValue("$ordinal", 0); command.Parameters.AddWithValue("$value", "");
            for (var index = 0; index < requested.Length; index++)
            { command.Parameters[0].Value = index; command.Parameters[1].Value = requested[index]; command.ExecuteNonQuery(); }
            command.Parameters.Clear(); command.CommandText = "SELECT count(*) FROM tracks WHERE missing=0";
            var total = Convert.ToInt32(command.ExecuteScalar()); var processed = 0;
            progress.Report(new(0, total));
            using var artists = new ArtistIndexWriter(connection, transaction);
            command.Parameters.AddWithValue("$after", 0L);
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                command.CommandText = "SELECT rowid,id,artist,album_key,duration,updated_at FROM tracks WHERE rowid>$after AND missing=0 ORDER BY rowid LIMIT 200";
                var rows = new List<(long Row,string Id,string Credit,string Album,double Duration,long Added)>(200);
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) rows.Add((reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetDouble(4),reader.GetInt64(5)));
                if (rows.Count == 0) break;
                foreach (var row in rows)
                { cancellation.ThrowIfCancellationRequested(); artists.ReplaceGrouping(0,row.Id,row.Credit,row.Album,row.Duration,row.Added,cancellation); }
                processed += rows.Count; progress.Report(new(processed,total)); command.Parameters[0].Value = rows[^1].Row;
            }
            cancellation.ThrowIfCancellationRequested();
            command.CommandText = "UPDATE artist_import_state SET revision=revision+1 WHERE id=1; UPDATE library_state SET revision=revision+1 WHERE id=1;";
            command.ExecuteNonQuery(); transaction.Commit(); return new ArtistImportSettings(current.Revision + 1,requested);
        }, false, cancellation);
    }
}
