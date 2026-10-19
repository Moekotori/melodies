using System.Diagnostics;
using System.Globalization;
using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

internal static class ScanThroughputProbe
{
    public static async Task RunAsync(string root, string label, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        const int count = 2048;
        var lines = new List<string> { "scenario,phase,files,elapsedMs,discoveryMs,metadataMs,lookupMs,commitMs,batches,allocatedBytes,privateBytes" };
        foreach (var layout in new[] { "flat-wav", "album-mixed" })
        {
            var media = Path.Combine(root, layout); Directory.CreateDirectory(media);
            for (var i = 0; i < count; i++)
            {
                var directory = layout == "flat-wav" ? media : Path.Combine(media, "album-" + (i / 16).ToString("D3"));
                Directory.CreateDirectory(directory);
                var flac = layout == "album-mixed" && i % 2 == 1;
                var path = Path.Combine(directory, $"track-{i:D5}" + (flac ? ".flac" : ".wav"));
                if (File.Exists(path)) continue;
                if (flac) AudioFixture.Flac(path); else AudioFixture.Wave(path, $"Track {i:D5}");
            }
            await using var db = await LibraryDatabase.OpenAsync(Path.Combine(root, layout + "-" + label + "-" + Guid.NewGuid().ToString("N") + ".sqlite"), token);
            var store = new TimedStore(db);
            var discovery = new TimedDiscovery(new LegacyFolderDiscovery(store));
            var metadataTicks = 0L;
            var scan = new ScanCoordinator(store, discovery, () => new TimedMetadata(new LocalMetadataReader(),
                ticks => Interlocked.Add(ref metadataTicks, ticks)));
            foreach (var phase in new[] { "first", "incremental", "forced" })
            {
                discovery.Ticks = 0; metadataTicks = 0; store.Reset();
                var allocation = GC.GetTotalAllocatedBytes(true);
                var result = await scan.ScanAsync(new(media, DetectMissing: true, Force: phase == "forced"), cancellation: token);
                allocation = GC.GetTotalAllocatedBytes(true) - allocation;
                if (result.Status != ScanStatus.Completed || result.Failed != 0 || result.Discovered != count || result.MarkedMissing != 0 ||
                    phase == "first" && result.Inserted != count || phase == "incremental" && result.Unchanged != count ||
                    phase == "forced" && result.Updated != count)
                    throw new Exception("Throughput correctness: " + result);
                using var process = Process.GetCurrentProcess();
                var line = string.Join(',', layout, phase, count,
                    Number(result.ElapsedMilliseconds), Number(Ms(discovery.Ticks)), Number(Ms(metadataTicks)),
                    Number(Ms(store.LookupTicks)), Number(Ms(store.CommitTicks)), store.Batches, allocation, process.PrivateMemorySize64);
                lines.Add(line); Console.WriteLine(line);
            }
            // Query while a force scan writes; preserve the interactive lane and check final index facts.
            var writing = scan.ScanAsync(new(media, Force: true), cancellation: token);
            var maximum = 0.0;
            do
            {
                var watch = Stopwatch.StartNew(); await db.GetTracksAsync(new(50), token);
                maximum = Math.Max(maximum, watch.Elapsed.TotalMilliseconds);
                await Task.Delay(10, token);
            } while (!writing.IsCompleted);
            var final = await writing;
            if (final.Status != ScanStatus.Completed || (await db.GetStatsAsync(token)).Tracks != count) throw new Exception("Concurrent scan/query mismatch");
            Console.WriteLine($"QUERY_DURING_SCAN {layout} maxMs={maximum:F3}");
        }
        await File.WriteAllLinesAsync(Path.Combine(root, label + ".csv"), lines, token);
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    private static string Number(double number) => number.ToString("F3", CultureInfo.InvariantCulture);

    private sealed class TimedDiscovery(IFileDiscovery inner) : IFileDiscovery
    {
        public long Ticks;
        public IEnumerable<FileSnapshot> Enumerate(string root, bool full, CancellationToken token, bool force = false)
        {
            using var iterator = inner.Enumerate(root, full, token, force).GetEnumerator();
            while (true)
            {
                var start = Stopwatch.GetTimestamp(); var more = iterator.MoveNext(); Ticks += Stopwatch.GetTimestamp() - start;
                if (!more) yield break;
                yield return iterator.Current;
            }
        }
    }
    private sealed class TimedMetadata(IMetadataReader inner, Action<long> record) : IMetadataReader
    {
        public async ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, CancellationToken token)
        {
            var start = Stopwatch.GetTimestamp();
            try { return await inner.ReadAsync(file, token); }
            finally { record(Stopwatch.GetTimestamp() - start); }
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    private sealed class TimedStore(ILibraryStore inner) : ILibraryStore
    {
        public long LookupTicks, CommitTicks;
        public int Batches;
        public void Reset() { LookupTicks = CommitTicks = 0; Batches = 0; }
        public async Task<IReadOnlyDictionary<string, KnownTrack>> FindKnownAsync(IReadOnlyList<FileSnapshot> files, CancellationToken token)
        {
            var start = Stopwatch.GetTimestamp();
            try { return await inner.FindKnownAsync(files, token); }
            finally { Interlocked.Add(ref LookupTicks, Stopwatch.GetTimestamp() - start); }
        }
        public async Task<BatchReceipt> CommitScanBatchAsync(string job, long sequence, IReadOnlyList<ScanEntry> entries, CancellationToken token)
        {
            var start = Stopwatch.GetTimestamp();
            try { return await inner.CommitScanBatchAsync(job, sequence, entries, token); }
            finally { Interlocked.Add(ref CommitTicks, Stopwatch.GetTimestamp() - start); Interlocked.Increment(ref Batches); }
        }
        public Task<string> BeginScanAsync(string root, bool presence, CancellationToken token) => inner.BeginScanAsync(root, presence, token);
        public Task<long> FinishScanAsync(string job, ScanStatus state, long count, bool missing, string? error, CancellationToken token) => inner.FinishScanAsync(job, state, count, missing, error, token);
        public Task<IReadOnlyList<FileSnapshot>> ReadDefinitionTracksAsync(string path, string? after, CancellationToken token) => inner.ReadDefinitionTracksAsync(path, after, token);
        public Task<TrackPage> GetTracksAsync(TrackQuery query, CancellationToken token = default) => inner.GetTracksAsync(query, token);
        public Task<LibraryStats> GetStatsAsync(CancellationToken token = default) => inner.GetStatsAsync(token);
        public Task<IReadOnlyList<ScanIssue>> GetScanIssuesAsync(string job, int limit = 16, CancellationToken token = default) => inner.GetScanIssuesAsync(job, limit, token);
        public Task<IReadOnlyDictionary<string, SourceCacheItem>> ReadSourceCacheAsync(IReadOnlyList<FileSnapshot> files, CancellationToken token) => inner.ReadSourceCacheAsync(files, token);
        public Task StoreSourceCacheAsync(IReadOnlyList<SourceCacheItem> items, CancellationToken token) => inner.StoreSourceCacheAsync(items, token);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
