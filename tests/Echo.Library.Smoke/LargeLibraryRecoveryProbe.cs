using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;
using Microsoft.Data.Sqlite;

internal static class LargeLibraryRecoveryProbe
{
    public static async Task RunAsync(string root, CancellationToken cancellation)
    {
        var media = Path.Combine(root, "recover-files"); Directory.CreateDirectory(media);
        for (var i = 0; i < 256; i++) AudioFixture.Wave(Path.Combine(media, $"track-{i:000}.wav"), "Recovery");
        var path = Path.Combine(root, "recovery.sqlite");
        string interrupted;
        var files = new DirectoryDiscovery().Enumerate(media, false, cancellation).Take(128).ToArray();
        await using (var db = await LibraryDatabase.OpenAsync(path, cancellation))
        {
            interrupted = await db.BeginScanAsync(media, true, cancellation);
            await using var reader = new LocalMetadataReader();
            var entries = new List<ScanEntry>();
            foreach (var file in files) entries.Add(new(file, ScanEntryKind.Metadata, await reader.ReadAsync(file, cancellation)));
            var receipt = await db.CommitScanBatchAsync(interrupted, 1, entries, cancellation);
            var retry = await db.CommitScanBatchAsync(interrupted, 1, entries, cancellation);
            if (receipt != retry || (await db.GetStatsAsync(cancellation)).Tracks != 128)
                throw new InvalidOperationException("Batch receipt was not idempotent.");
            // Leave Running at close to model process interruption after a durable batch.
        }
        await using (var db = await LibraryDatabase.OpenAsync(path, cancellation))
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            using var state = connection.CreateCommand(); state.CommandText = "SELECT state FROM scan_jobs WHERE id=$job";
            state.Parameters.AddWithValue("$job", interrupted);
            if ((string?)state.ExecuteScalar() != "Interrupted") throw new InvalidOperationException("Interrupted job not recovered.");
            var scanner = new ScanCoordinator(db, new LegacyFolderDiscovery(db), () => new LocalMetadataReader());
            var result = await scanner.ScanAsync(new(media, DetectMissing: true), cancellation: cancellation);
            if (result.Unchanged != 128 || result.Inserted != 128 || result.Failed != 0 || result.MarkedMissing != 0)
                throw new InvalidOperationException("Committed work was not reused on restart.");
            var cancelled = await db.BeginScanAsync(media, true, cancellation);
            await db.FinishScanAsync(cancelled, ScanStatus.Cancelled, 0, true, "cancelled", cancellation);
            if ((await db.GetStatsAsync(cancellation)).Missing != 0) throw new InvalidOperationException("Cancellation marked unvisited files missing.");
            File.Delete(files[0].Path);
            var completed = await scanner.ScanAsync(new(media, DetectMissing: true), cancellation: cancellation);
            var stats = await db.GetStatsAsync(cancellation);
            if (completed.MarkedMissing != 1 || stats.Tracks != 256 || stats.Missing != 1)
                throw new InvalidOperationException("Complete coverage missing detection failed.");
        }
        Console.WriteLine("RECOVERY receiptReused=true committedReused=128 cancelledMissing=0 completedMissing=1");
    }
}
