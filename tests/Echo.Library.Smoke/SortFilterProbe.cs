using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;

internal static class SortFilterProbe
{
    public static async Task RunAsync(string root, CancellationToken cancellation)
    {
        Directory.CreateDirectory(root);
        await using var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "sort-filter.sqlite"), cancellation);
        var job = await db.BeginScanAsync(root, false, cancellation);
        var rows = new[]
        {
            ("Same", "A", "Z", 120.0, 2, (int?)2020),
            ("Same", "A", "Z", 120.4, 1, (int?)2020),
            ("Same", "A", "Z", 122.0, 3, (int?)null),
            ("Other", "B", "A", 60.0, 1, (int?)2026),
            ("Same", "B", "A", 120.0, 2, (int?)2026)
        };
        var entries = rows.Select((row, i) =>
        {
            var path = Path.Combine(root, $"{i}.flac");
            return new ScanEntry(new(path, LibraryPath.Key(path), 1000000 + i * 100, 100 + i), ScanEntryKind.Metadata,
                new(row.Item1, row.Item2, row.Item3, "", row.Item4, "FLAC", 48000, 24, row.Item5, 1, row.Item6, Bitrate: 1000000 - i * 100000, Bpm: 120 + i));
        }).ToArray();
        await db.CommitScanBatchAsync(job, 1, entries, cancellation);
        await db.FinishScanAsync(job, ScanStatus.Completed, entries.Length, false, null, cancellation);
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var duplicates = await db.GetTracksAsync(new(Filter: new(DuplicatesOnly: true)), cancellation);
        Check(duplicates.Items.Count == 2 && duplicates.Items.All(row => row.Artist == "A" && row.DurationSeconds < 121), "duplicate identity and tolerance");
        Check((await db.GetTracksAsync(new(Filter: new(DuplicatesOnly: true, Year: 2026)), cancellation)).Items.Count == 0, "filter intersection");
        var albums = await db.GetTracksAsync(new(Order: [new(TrackSort.Album, true)]), cancellation);
        Check(albums.Items.Take(3).Select(row => row.TrackNumber).SequenceEqual(new int?[] { 1, 2, 3 }), "descending albums retain ascending track order");
        var years = await db.GetTracksAsync(new(Order: [new(TrackSort.Year)]), cancellation);
        Check(years.Items[^1].Path.EndsWith("2.flac"), "unknown years sort last");
        foreach (var sort in new[] { TrackSort.Random, TrackSort.LastPlayed, TrackSort.PlayCount, TrackSort.Bpm, TrackSort.AudioSpec, TrackSort.Quality, TrackSort.Bitrate })
        {
            var query = new TrackQuery(2, Order: [new(sort)], RandomSeed: 15485863);
            var ids = new HashSet<string>(); TrackCursor? cursor = null;
            do
            {
                var page = await db.GetTracksAsync(query with { After = cursor }, cancellation);
                Check(page.Items.All(row => ids.Add(row.Id)), "page overlap: " + sort);
                cursor = page.Next;
            } while (cursor is not null);
            Check(ids.Count == entries.Length, "page completeness: " + sort);
        }
        var bitrate = await db.GetTracksAsync(new(Order: [new(TrackSort.Bitrate, true)]), cancellation);
        Check(bitrate.Items[0].Path.EndsWith("0.flac"), "stored bitrate ordering");
        var bpmOrder = await db.GetTracksAsync(new(Order: [new(TrackSort.Bpm, true)]), cancellation);
        Check(bpmOrder.Items[0].Path.EndsWith("4.flac"), "stored tempo ordering");
        var first = await db.GetTracksAsync(new(1, Order: [new(TrackSort.Random)], RandomSeed: 15485863), cancellation);
        try { await db.GetTracksAsync(new(1, After: first.Next, Order: [new(TrackSort.Random)], RandomSeed: 32452843), cancellation); throw new InvalidOperationException("shuffle cursor reused"); }
        catch (ArgumentException) { }
        Console.WriteLine("Sort/filter: duplicate identity, intersections, album track order, unknown years, seven paged sort fields and shuffle cursor passed.");
    }
}
