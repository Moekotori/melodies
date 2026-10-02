using System.Diagnostics;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal static class AlbumProbe
{
    public static async Task RunAsync(string root, CancellationToken cancellation)
    {
        var path = Path.Combine(root, "albums.sqlite");
        var media = Path.Combine(root, "media");
        string[] titles = ["Blue Hour", "Distant Signals", "Evergreen", "Falling Into Place", "For Emma", "In Rainbows", "Late Night Tales", "Moon Safari", "Night Walk", "Ocean Eyes", "Quiet Life", "Sunset Radio", "The New Abnormal", "There Is Love", "Under the Stars", "Vespertine", "Warm Light", "We Are Here", "夜航", "春日来信", "浮光", "海边散步", "白日梦", "风与远方"];
        await using var db = await LibraryDatabase.OpenAsync(path, cancellation);
        var job = await db.BeginScanAsync(media, false, cancellation);
        long sequence = 0;
        var timer = Stopwatch.StartNew();
        for (var offset = 0; offset < 50_000; offset += 200)
        {
            var batch = Enumerable.Range(offset, 200).Select(index =>
            {
                var group = index / 5;
                var file = Path.Combine(media, $"album-{group:00000}", $"track-{index:00000}.flac");
                return new ScanEntry(new(file, LibraryPath.Key(file), 1000, 123), ScanEntryKind.Metadata,
                    new($"Track {index % 5 + 1}", $"Artist {group % 12 + 1:00}",
                        group < titles.Length ? titles[group] : $"~ Archive {group:00000}",
                        $"Artist {group % 12 + 1:00}", 180 + index % 5, group % 2 == 0 ? "FLAC" : "MP3", 48000, 24,
                        TrackNumber: index % 5 + 1, DiscNumber: 1, Year: 2000 + group % 25));
            }).ToArray();
            await db.CommitScanBatchAsync(job, ++sequence, batch, cancellation);
        }
        await db.FinishScanAsync(job, ScanStatus.Completed, 50_000, false, null, cancellation);
        var insertMs = timer.Elapsed.TotalMilliseconds;
        var page = await db.GetAlbumsAsync(new(), cancellation);
        Require(page.Total == 10000 && page.Items.Count == 200 && page.Items.All(a => a.TrackCount == 5), "materialized count");
        var album = page.Items.First(a => a.Title == "Blue Hour");
        var tracks = await db.GetAlbumTracksAsync(new(album.Id), cancellation);
        Require(tracks.Items.Count == 5 && tracks.Items[0].Title == "Track 1" && tracks.Items[^1].Title == "Track 5", "disc/track order");
        Require((await db.GetAlbumsAsync(new(Search: "Blue"), cancellation)).Total == 1, "album FTS");
        Require((await db.GetAlbumsAsync(new(LosslessOnly: true), cancellation)).Total == 5000, "lossless aggregate");
        var latencies = new List<double>();
        foreach (var sort in Enum.GetValues<AlbumSort>())
            foreach (var descending in new[] { false, true })
            {
                timer.Restart();
                var deep = await db.GetAlbumsAsync(new(9600, Sort: sort, Descending: descending), cancellation);
                Require(deep.Items.Count == 200, "deep page");
                latencies.Add(timer.Elapsed.TotalMilliseconds);
            }

        // A metadata move must subtract the old album and atomically create a new one.
        job = await db.BeginScanAsync(media, false, cancellation);
        var first = tracks.Items[0];
        var fileSnapshot = new FileSnapshot(first.Path, LibraryPath.Key(first.Path), 2000, 124);
        var moved = new TrackMetadata("Moved", "Artist 01", "Moved Album", "Artist 01", 90, "MP3", 44100, 16, Year: 2026);
        await db.CommitScanBatchAsync(job, 1, [new(fileSnapshot, ScanEntryKind.Metadata, moved, first.Id)], cancellation);
        Require((await db.GetAlbumsAsync(new(Search: "Blue"), cancellation)).Items[0].TrackCount == 4, "old album decremented");
        Require((await db.GetAlbumsAsync(new(Search: "Moved"), cancellation)).Items[0].TrackCount == 1, "new album created");
        try { await db.GetAlbumsAsync(new(200, Revision: page.Revision), cancellation); throw new Exception("revision accepted"); }
        catch (LibraryChangedException) { }
        await db.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, cancellation);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await db.GetAlbumsAsync(new(), cancelled.Token); throw new Exception("cancellation ignored"); }
        catch (OperationCanceledException) { }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        foreach (var sort in new[] { "sort_key", "artist_key", "year", "added", "duration", "track_count" })
        {
            command.CommandText = $"EXPLAIN QUERY PLAN SELECT id FROM albums ORDER BY {sort},id LIMIT 200 OFFSET 9600";
            using var reader = command.ExecuteReader();
            var indexed = false;
            while (reader.Read()) indexed |= reader.GetString(3).Contains("USING COVERING INDEX albums_", StringComparison.Ordinal);
            Require(indexed, "sort index " + sort);
        }
        latencies.Sort();
        Console.WriteLine($"Album checks passed: 50,000 tracks / 10,000 albums; insert={insertMs:F1}ms; indexed deep-page samples={latencies.Count}; median={latencies[latencies.Count / 2]:F2}ms; max={latencies[^1]:F2}ms.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Album probe: " + message); }

    public static async Task ReadAsync(string path, CancellationToken cancellation)
    {
        await using var database = await LibraryDatabase.OpenAsync(path, cancellation);
        var samples = new List<double>();
        foreach (var sort in Enum.GetValues<AlbumSort>())
            foreach (var descending in new[] { false, true })
            {
                var timer = Stopwatch.StartNew();
                var page = await database.GetAlbumsAsync(new(9600, Sort: sort, Descending: descending), cancellation);
                samples.Add(timer.Elapsed.TotalMilliseconds);
                Require(page.Items.Count == 200, "indexed deep-page read");
            }
        samples.Sort();
        Console.WriteLine($"Current album query samples={samples.Count}; median={samples[samples.Count / 2]:F2}ms; max={samples[^1]:F2}ms; includes personal state columns and revision count cache.");
    }
}
