using System.Diagnostics;
using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

internal sealed record ProbeResult(double PreparedBatchMs, double PerRecordTransactionMs,
    double FastMetadataMs, double? ExistingNativeMetadataMs, double FirstScanMs, double IncrementalScanMs,
    long AllocatedBytesDuringIncremental, long ProcessPrivateBytes, int Records);

internal static class PerformanceProbe
{
    public static async Task<ProbeResult> RunAsync(string root, string? native, CancellationToken cancellation)
    {
        const int records = 512;
        var media = Path.Combine(root, "perf-media");
        Directory.CreateDirectory(media);
        for (var i = 0; i < records; i++) AudioFixture.Wave(Path.Combine(media, $"perf-{i:0000}.wav"), $"Perf {i:0000}");
        var snapshots = new DirectoryDiscovery().Enumerate(media, false, cancellation).ToArray();
        await using var reader = new LocalMetadataReader(native);
        var entries = new ScanEntry[records];
        for (var i = 0; i < records; i++)
            entries[i] = new(snapshots[i], ScanEntryKind.Metadata, await reader.ReadAsync(snapshots[i], cancellation));

        async Task<double> DatabaseProbe(string path, int batch)
        {
            await using var db = await LibraryDatabase.OpenAsync(Path.Combine(root, path), cancellation);
            var job = await db.BeginScanAsync(media, false, cancellation);
            var watch = Stopwatch.StartNew();
            long sequence = 0;
            for (var i = 0; i < records; i += batch)
                await db.CommitScanBatchAsync(job, ++sequence, entries.AsSpan(i, Math.Min(batch, records - i)).ToArray(), cancellation);
            var elapsed = watch.Elapsed.TotalMilliseconds;
            await db.FinishScanAsync(job, ScanStatus.Completed, records, false, null, cancellation);
            return elapsed;
        }
        // Both sides use this module's identical schema/FTS/durability. This is a modeled baseline, not old ECHO end-to-end.
        var perRecord = await DatabaseProbe("per-record.sqlite", 1);
        var batchMs = await DatabaseProbe("batch.sqlite", 128);
        var clock = Stopwatch.StartNew();
        foreach (var file in snapshots.Take(128)) await reader.ReadAsync(file, cancellation);
        var fast = clock.Elapsed.TotalMilliseconds;
        double? oldNative = null;
        if (native is not null)
        {
            await using var cpp = new NativeMetadataReader(native);
            // Exclude startup from both warm metadata measurements.
            await cpp.ReadAsync(snapshots[0], cancellation);
            clock.Restart();
            foreach (var file in snapshots.Take(128)) await cpp.ReadAsync(file, cancellation);
            oldNative = clock.Elapsed.TotalMilliseconds;
        }
        await using var scanDb = await LibraryDatabase.OpenAsync(Path.Combine(root, "scan-perf.sqlite"), cancellation);
        var scan = new ScanCoordinator(scanDb, new DirectoryDiscovery(), () => new LocalMetadataReader(native));
        var first = await scan.ScanAsync(new(media), cancellation: cancellation);
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var again = await scan.ScanAsync(new(media), cancellation: cancellation);
        allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        if (again.Unchanged != records || again.Updated != 0 || again.Failed != 0)
            throw new InvalidOperationException("Incremental probe did not skip all records.");
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new(batchMs, perRecord, fast, oldNative, first.ElapsedMilliseconds,
            again.ElapsedMilliseconds, allocated, process.PrivateMemorySize64, records);
    }
}
