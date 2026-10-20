using System.Diagnostics;
using System.Globalization;
using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

internal static class PagePerformanceProbe
{
    public static async Task RunAsync(string root, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var media = Path.Combine(root, "media");
        await using var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "pages.sqlite"), token);
        const int count = 6000;
        var job = await db.BeginScanAsync(media, false, token);
        for (var offset = 0; offset < count; offset += 100)
        {
            var entries = Enumerable.Range(offset, Math.Min(100, count - offset)).Select(i =>
            {
                var path = Path.Combine(media, $"album-{i / 20:D4}", $"track-{i:D6}.flac");
                return new ScanEntry(new(path, LibraryPath.Key(path), 1000, 123), ScanEntryKind.Metadata,
                    new($"Track {i:D6}", $"Artist {i / 100:D3}", $"Album {i / 20:D4}", "", 180,
                        "FLAC", 48000, 24, Genre: "Rock"));
            }).ToArray();
            await db.CommitScanBatchAsync(job, offset / 100 + 1, entries, token);
        }
        await db.FinishScanAsync(job, ScanStatus.Completed, count, false, null, token);
        var rows = (await db.GetTracksAsync(new(200), token)).Items;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var now = DateTimeOffset.Now.ToUnixTimeSeconds();
        for (var i = 0; i < 1200; i++)
        {
            var row = rows[i % rows.Count];
            await db.RecordListeningAsync(new($"listen-{i}", row.Id, row.Path, now - i * 60,
                now - i * 60 + 30, 30, now - i * 60 + 30), token);
        }

        var output = new List<string> { "scenario,medianMs,p95Ms,allocatedBytesPerCall" };
        await Measure("home", async () => { await db.GetHomeAsync(today, token); });
        await Measure("folder-page", async () => { await db.GetFolderTracksAsync(new(media, Offset: 200), token); });
        await Measure("songs", async () => { await db.GetTracksAsync(new(200), token); });
        await Measure("albums", async () => { await db.GetAlbumsAsync(new(), token); });
        await Measure("artists", async () => { await db.GetArtistsAsync(new(), token); });
        await Measure("genres", async () => { await db.GetGenresAsync(new(), token); });
        await File.WriteAllLinesAsync(Path.Combine(root, "results.csv"), output, token);

        var home = await db.GetHomeAsync(today, token);
        Check(home.Library.Tracks == count && home.Library.Folders == 300, "home counts");
        Check(home.RecentPlayed.Count == 8 && home.RecentPlayed[0].Title == "Album 0000", "recent history order");
        var first = await db.GetFolderTracksAsync(new(media), token);
        var next = await db.GetFolderTracksAsync(new(media, Offset: 200, Sort: FolderTrackSort.Title), token);
        Check(first.Total == count && next.Total == count && next.DurationSeconds == count * 180, "folder summary");
        Check(!first.Items.Select(r => r.Id).Intersect(next.Items.Select(r => r.Id)).Any(), "disjoint pages");
        Check((await db.GetFolderTracksAsync(new(media, Search: "absent"), token)).Total == 0, "search isolation");
        Check((await db.GetFolderTracksAsync(new(media, Recursive: false), token)).Total == 0, "recursive isolation");
        var live = new ListeningEntry("live", rows[0].Id, rows[0].Path, now + 1, now + 1, 1);
        await db.RecordListeningAsync(live, token);
        var before = await db.GetHomeAsync(today, token);
        await db.RecordListeningAsync(live with { PlayedSeconds = 20, UpdatedUnix = now + 20 }, token);
        var after = await db.GetHomeAsync(today, token);
        Check(after.Listening.WeekSeconds == before.Listening.WeekSeconds + 19, "progress freshness");
        job = await db.BeginScanAsync(media, false, token);
        var addedPath = Path.Combine(media, "new-folder", "new.flac");
        await db.CommitScanBatchAsync(job, 1, [new(new(addedPath, LibraryPath.Key(addedPath), 2000, 123),
            ScanEntryKind.Metadata, new("New", "New artist", "New album", "", 90, "MP3", 44100, 16))], token);
        await db.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, token);
        Check((await db.GetHomeAsync(today, token)).Library is { Tracks: count + 1, Folders: 301 }, "home invalidation");
        var updated = await db.GetFolderTracksAsync(new(media), token);
        Check(updated.Total == count + 1 && updated.DurationSeconds == count * 180 + 90 && updated.LosslessCount == count, "folder invalidation");
        Check((await db.GetFolderTracksAsync(new(media, LosslessOnly: true), token)).Total == count, "lossless isolation");
        try { await db.GetFolderTracksAsync(new(media, Revision: first.Revision), token); throw new Exception("stale revision accepted"); }
        catch (LibraryChangedException) { }
        var resources = new FolderTrackQuery(media, Insight: FolderInsight.MissingCover);
        await db.StoreFolderResourcesAsync([new(rows[0].Id, false, true)], token);
        Check((await db.GetFolderTracksAsync(resources, token)).Total == 1, "resource filter initial");
        await db.StoreFolderResourcesAsync([new(rows[0].Id, true, true)], token);
        Check((await db.GetFolderTracksAsync(resources, token)).Total == 0, "resource facts freshness without revision");
        var paths = new[] { Path.Combine(media, "album-0000") };
        var combined = new FolderTrackQuery(media, Paths: paths);
        Check((await db.GetFolderTracksAsync(combined, token)).Total == 20, "combined folder scope");
        paths[0] = Path.Combine(media, "new-folder");
        Check((await db.GetFolderTracksAsync(combined, token)).Total == 1, "combined mutable input");
        for (var i = 0; i < 18; i++) await db.GetFolderTracksAsync(new(media, Search: $"absent-{i}"), token);
        Check((await db.GetFolderTracksAsync(new(media), token)).Total == count + 1, "bounded cache eviction");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await db.GetFolderTracksAsync(new(media), cancelled.Token); throw new Exception("cancel ignored"); }
        catch (OperationCanceledException) { }
        await ScanDuringReads();
        Console.WriteLine("PAGE_PERFORMANCE_PASSED checks=19");

        async Task ScanDuringReads()
        {
            var directory = Path.Combine(media, "live-scan"); Directory.CreateDirectory(directory);
            for (var i = 0; i < 512; i++) AudioFixture.Wave(Path.Combine(directory, $"live-{i:D4}.wav"), $"Live {i}");
            var scanner = new ScanCoordinator(db, new DirectoryDiscovery(), () => new LocalMetadataReader());
            var scanning = scanner.ScanAsync(new(directory), cancellation: token);
            var maximum = 0d; var samples = 0;
            do
            {
                var watch = Stopwatch.StartNew(); await db.GetHomeAsync(today, token);
                maximum = Math.Max(maximum, watch.Elapsed.TotalMilliseconds); samples++;
                watch.Restart(); await db.GetFolderTracksAsync(new(media), token);
                maximum = Math.Max(maximum, watch.Elapsed.TotalMilliseconds); samples++;
                await Task.Delay(10, token);
            } while (!scanning.IsCompleted);
            var result = await scanning;
            Check(result is { Status: ScanStatus.Completed, Inserted: 512, Failed: 0 }, "real scan with concurrent pages");
            Check((await db.GetHomeAsync(today, token)).Library.Tracks == count + 513 &&
                (await db.GetFolderTracksAsync(new(media), token)).Total == count + 513, "visible final scan counts");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"PAGES_DURING_SCAN samples={samples} maxMs={maximum:F3} scanMs={result.ElapsedMilliseconds:F3}"));
        }

        async Task Measure(string name, Func<Task> read)
        {
            await read();
            var times = new double[12];
            var allocated = GC.GetTotalAllocatedBytes(true);
            for (var i = 0; i < times.Length; i++)
            {
                var watch = Stopwatch.StartNew(); await read(); times[i] = watch.Elapsed.TotalMilliseconds;
            }
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            Array.Sort(times);
            var line = string.Create(CultureInfo.InvariantCulture, $"{name},{times[6]:F3},{times[11]:F3},{allocated / times.Length}");
            output.Add(line); Console.WriteLine(line);
        }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
