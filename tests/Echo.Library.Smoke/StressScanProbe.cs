using System.Diagnostics;
using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

internal static class StressScanProbe
{
    public static async Task RunAsync(string root, CancellationToken token)
    {
        StressLibraryFixture.ValidateRoot(root, false);
        var directory = Path.Combine(StressLibraryFixture.Media(root), "real-wav-scan");
        Directory.CreateDirectory(directory);
        for (var i = 0; i < 4096; i++)
            AudioFixture.Wave(Path.Combine(directory, $"live-{i:D4}.wav"), $"Stress live {i}");
        await using var metrics = new StressReadMetrics(root, "scan");
        await using var db = await LibraryDatabase.OpenAsync(StressLibraryFixture.Database(root), token);
        var before = (await db.GetStatsAsync(token)).Tracks;
        var scanner = new ScanCoordinator(db, new DirectoryDiscovery(), () => new LocalMetadataReader());
        var scan = scanner.ScanAsync(new(directory, Force: true), cancellation: token);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var media = StressLibraryFixture.Media(root);
        var observations = new List<string>();
        await Task.WhenAll(
            Read("home", async t => { await db.GetHomeAsync(today, t); }),
            Read("songs", async t => { await db.GetTracksAsync(new(200), t); }),
            Read("artists", async t => { await db.GetArtistsAsync(new(), t); }),
            Read("folder-tracks", async t => { await db.GetFolderTracksAsync(new(media), t); }));
        var report = await scan;
        var stats = await db.GetStatsAsync(token);
        if (report.Status != ScanStatus.Completed || report.Failed != 0 || report.Inserted + report.Updated != 4096 ||
            stats.Tracks != (before == StressLibraryFixture.Tracks ? before + 4096 : before))
            throw new InvalidOperationException("Concurrent scan did not complete accurately.");
        var home = await db.GetHomeAsync(today, token);
        if (home.Library.Tracks != stats.Tracks) throw new InvalidOperationException("Home failed to reflect scan.");
        observations.Insert(0, "scenario,samples,medianMs,p95Ms,maxMs");
        await File.WriteAllLinesAsync(Path.Combine(root, "scan-concurrent.csv"), observations, token);
        Console.WriteLine(FormattableString.Invariant($"STRESS_SCAN_COMPLETE scanMs={report.ElapsedMilliseconds:F1} inserted={report.Inserted} updated={report.Updated} totalTracks={stats.Tracks}"));

        async Task Read(string name, Func<CancellationToken, Task> read)
        {
            var samples = new List<double>(256);
            do
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
                budget.CancelAfter(TimeSpan.FromSeconds(10));
                var watch = Stopwatch.StartNew(); await read(budget.Token); samples.Add(watch.Elapsed.TotalMilliseconds);
                await Task.Delay(50, token);
            } while (!scan.IsCompleted && samples.Count < 256);
            samples.Sort();
            var line = FormattableString.Invariant($"{name},{samples.Count},{samples[samples.Count / 2]:F3},{samples[(int)Math.Ceiling(samples.Count * .95) - 1]:F3},{samples[^1]:F3}");
            lock (observations) observations.Add(line);
            Console.WriteLine("STRESS_SCAN " + line);
        }
    }
}
