using System.Diagnostics;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal sealed record ScaleResult(int Records, double InsertMs, double QueryDuringWriteMaxMs,
    double PageQueryP95Ms, int QuerySamples, int TraversedRows, double TraverseMs, long ProcessPrivateBytes,
    bool UsesPageIndex, int ExtraChecksPassed);

internal static class ScaleProbe
{
    public static async Task<ScaleResult> RunAsync(string root, CancellationToken cancellation)
    {
        const int records = 50_000;
        var media = Path.Combine(root, "generated-database-paths");
        var path = Path.Combine(root, "scale.sqlite");
        await using var db = await LibraryDatabase.OpenAsync(path, cancellation);
        var job = await db.BeginScanAsync(media, false, cancellation);
        var watch = Stopwatch.StartNew();
        var latencies = new List<double>();
        var metadata = new TrackMetadata("seed", "Scale Artist", "Scale Album", "", 180, "FLAC", 48000, 24);
        long sequence = 0;
        for (var offset = 0; offset < records; offset += 128)
        {
            var batch = new ScanEntry[Math.Min(128, records - offset)];
            for (var i = 0; i < batch.Length; i++)
            {
                var filePath = Path.Combine(media, $"track-{offset + i:000000}.flac");
                batch[i] = new(new(filePath, LibraryPath.Key(filePath), 100_000, 123),
                    ScanEntryKind.Metadata, metadata with { Title = $"Track {offset + i:000000}" });
            }
            var writing = db.CommitScanBatchAsync(job, ++sequence, batch, cancellation);
            if (offset % 1024 == 0)
            {
                var queryWatch = Stopwatch.StartNew();
                await db.GetTracksAsync(new(200), cancellation);
                latencies.Add(queryWatch.Elapsed.TotalMilliseconds);
            }
            await writing;
        }
        var insertedMs = watch.Elapsed.TotalMilliseconds;
        await db.FinishScanAsync(job, ScanStatus.Completed, records, false, null, cancellation);
        if ((await db.GetStatsAsync(cancellation)).Tracks != records) throw new InvalidOperationException("Scale count mismatch.");
        var duringMax = latencies.Max();
        latencies.Clear();
        for (var i = 0; i < 32; i++)
        {
            watch.Restart();
            var page = await db.GetTracksAsync(new(200, i % 2 == 0 ? null : "Scale"), cancellation);
            if (page.Items.Count != 200) throw new InvalidOperationException("Scale first page mismatch.");
            latencies.Add(watch.Elapsed.TotalMilliseconds);
        }
        latencies.Sort();
        watch.Restart();
        TrackCursor? cursor = null;
        var count = 0;
        string? prior = null;
        do
        {
            var page = await db.GetTracksAsync(new(200, After: cursor), cancellation);
            foreach (var row in page.Items)
            {
                if (prior is not null && StringComparer.Ordinal.Compare(prior, row.SortKey) >= 0)
                    throw new InvalidOperationException("Scale order mismatch.");
                prior = row.SortKey; count++;
            }
            cursor = page.Next;
        } while (cursor is not null);
        var traverse = watch.Elapsed.TotalMilliseconds;
        if (count != records) throw new InvalidOperationException("Scale traversal lost rows.");
        var usesIndex = false;
        using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                EXPLAIN QUERY PLAN SELECT id,title FROM tracks
                WHERE missing=0 AND (sort_key,id)>('TRACK 040000','')
                ORDER BY sort_key,id LIMIT 201
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read()) usesIndex |= reader.GetString(3).Contains("tracks_page", StringComparison.Ordinal);
        }
        if (!usesIndex) throw new InvalidOperationException("Page index unused.");
        var extra = await AccuracyProbe.RunAsync(Path.Combine(root, "accuracy"), cancellation);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new(records, insertedMs, duringMax, latencies[(int)Math.Ceiling(latencies.Count * .95) - 1],
            latencies.Count, count, traverse, process.PrivateMemorySize64, usesIndex, extra);
    }
}
