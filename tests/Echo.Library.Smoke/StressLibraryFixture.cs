using System.Diagnostics;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;

internal static class StressLibraryFixture
{
    public const int Tracks = 400_000;
    public const int Artists = 100_000;
    public static string Media(string root) => Path.Combine(root, "media");
    public static string Database(string root) => Path.Combine(root, "stress.sqlite");
    public static string ArtistFolder(string root, int artist) =>
        Path.Combine(Media(root), $"shard-{artist / 1000:D3}", $"artist-{artist:D6}");

    public static void ValidateRoot(string root, bool create)
    {
        const string identity = "ECHO_SYNTHETIC_400000_TRACKS_100000_ARTISTS_V1";
        var marker = Path.Combine(root, "fixture.identity");
        if (File.Exists(marker))
        {
            if (File.ReadAllText(marker) != identity) throw new InvalidOperationException("Wrong fixture identity.");
            return;
        }
        if (!create || File.Exists(Database(root))) throw new InvalidOperationException("An explicit stress fixture is required.");
        Directory.CreateDirectory(root); File.WriteAllText(marker, identity);
    }

    public static async Task CreateAsync(string root, CancellationToken token)
    {
        ValidateRoot(root, true);
        await using var db = await LibraryDatabase.OpenAsync(Database(root), token);
        var stats = await db.GetStatsAsync(token);
        if (stats.Tracks > Tracks || stats.Missing != 0) throw new InvalidOperationException("Unexpected fixture rows.");
        var watch = Stopwatch.StartNew();
        var allocated = GC.GetTotalAllocatedBytes(true);
        var job = await db.BeginScanAsync(Media(root), false, token);
        long sequence = 0;
        using var log = new StreamWriter(Path.Combine(root, "seed.csv"), append: true);
        if (log.BaseStream.Length == 0) log.WriteLine("tracks,elapsedMs,allocatedBytes,privateBytes");
        // Every row uses the production batch writer, triggers, FTS and derived indexes.
        // Restart resumes after committed contiguous batches; only 256 records are resident.
        for (var offset = checked((int)stats.Tracks); offset < Tracks; offset += 256)
        {
            var batch = new ScanEntry[Math.Min(256, Tracks - offset)];
            for (var j = 0; j < batch.Length; j++)
            {
                var i = offset + j; var artist = i / 4;
                var path = Path.Combine(ArtistFolder(root, artist), $"track-{i:D6}.flac");
                batch[j] = new(new(path, LibraryPath.Key(path), 25_000_000, 638000000000000000L), ScanEntryKind.Metadata,
                    new($"Track {i:D6}", $"Artist {artist:D6}", $"Album {artist:D6}", $"Artist {artist:D6}",
                        180 + i % 120, "FLAC", 48000, 24, TrackNumber: i % 4 + 1, Year: 1980 + artist % 45,
                        Genre: $"Genre {artist % 100:D3}"));
            }
            await db.CommitScanBatchAsync(job, ++sequence, batch, token);
            if ((offset + batch.Length) % 8192 == 0 || offset + batch.Length == Tracks)
            {
                using var process = Process.GetCurrentProcess(); process.Refresh();
                var line = FormattableString.Invariant($"{offset + batch.Length},{watch.Elapsed.TotalMilliseconds:F1},{GC.GetTotalAllocatedBytes(true) - allocated},{process.PrivateMemorySize64}");
                log.WriteLine(line); log.Flush(); Console.WriteLine("STRESS_SEED " + line);
            }
        }
        await db.FinishScanAsync(job, ScanStatus.Completed, Tracks, false, null, token);
        Console.WriteLine($"STRESS_SEED_COMPLETE tracks={Tracks} artists={Artists} logicalFolders={Artists}");
    }

    public static async Task HistoryAsync(string root, CancellationToken token)
    {
        ValidateRoot(root, false);
        var ready = Path.Combine(root, "history.ready");
        if (File.Exists(ready)) return;
        await using var db = await LibraryDatabase.OpenAsync(Database(root), token);
        if ((await db.GetStatsAsync(token)).Tracks != Tracks) throw new InvalidOperationException("Complete the fixture first.");
        var now = DateTimeOffset.Now.ToUnixTimeSeconds();
        for (var offset = 0; offset < 10000; offset += 200)
        {
            var page = await db.GetTracksAsync(new(200, Offset: offset), token);
            for (var j = 0; j < page.Items.Count; j++)
            {
                var row = page.Items[j]; var i = offset + j; var started = now - i * 300;
                await db.RecordListeningAsync(new($"stress-history-{i}", row.Id, row.Path, started, started + 90,
                    90, started + 90), token);
            }
            if ((offset + 200) % 2000 == 0) Console.WriteLine($"STRESS_HISTORY rows={offset + 200}");
        }
        await File.WriteAllTextAsync(ready, "10000", token);
    }
}
