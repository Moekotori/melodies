using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal static class LargeLibraryMaintenanceProbe
{
    public static async Task RunAsync(string root, CancellationToken cancellation)
    {
        var path = Path.Combine(root, "maintenance.sqlite");
        var audio = Path.Combine(root, "seed.flac");
        var file = new FileSnapshot(audio, LibraryPath.Key(audio), 100, 100);
        var metadata = new TrackMetadata("Seed", "Artist", "Album", "Artist", 60, "FLAC", 48000, 24);
        await using (var db = await LibraryDatabase.OpenAsync(path, cancellation))
        {
            var job = await db.BeginScanAsync(root, false, cancellation);
            await db.CommitScanBatchAsync(job, 1, [new(file, ScanEntryKind.Metadata, metadata)], cancellation);
            await db.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, cancellation);
            var track = (await db.GetTracksAsync(new(1), cancellation)).Items[0];
            await db.RecordListeningAsync(new("played", track.Id, track.Path, 1000, 1030, 30, EndedUnix: 1030), cancellation);
        }
        // Own the isolated fixture while simulating a v17 upgrade and a previously full source cache.
        using (var owner = new FileStream(path + ".writer.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        using (var seed = Connect(path, false))
        {
            using var command = seed.CreateCommand();
            command.CommandText = """
                PRAGMA user_version=17;
                WITH RECURSIVE numbers(n) AS(SELECT 0 UNION ALL SELECT n+1 FROM numbers WHERE n<262271)
                INSERT INTO scan_source_cache SELECT printf('cache-%06d',n),1,1,1,'{}',100 FROM numbers;
                """;
            command.ExecuteNonQuery();
        }
        await using (var db = await LibraryDatabase.OpenAsync(path, cancellation))
        {
            if (!Directory.GetFiles(root, "maintenance.sqlite.before-v18-*.sqlite").Any())
                throw new InvalidOperationException("Upgrade did not make a consistent backup.");
            var extra = file with { Path = audio + "2", PathKey = LibraryPath.Key(audio + "2") };
            var job = await db.BeginScanAsync(root, false, cancellation);
            await db.CommitScanBatchAsync(job, 1, [new(extra, ScanEntryKind.Metadata, metadata)], cancellation);
            await db.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, cancellation);
            using var read = Connect(path, true);
            using var check = read.CreateCommand();
            check.CommandText = "SELECT track_count || ':' || play_count || ':' || last_played FROM albums";
            if ((string?)check.ExecuteScalar() != "2:1:1000") throw new InvalidOperationException("Insert duplicated/lost album history.");
            // Force UPDATE instead of INSERT; an idempotent metadata refresh must not add a play.
            var known = (await db.FindKnownAsync([extra], cancellation))[extra.PathKey];
            job = await db.BeginScanAsync(root, false, cancellation);
            await db.CommitScanBatchAsync(job, 1, [new(extra, ScanEntryKind.Metadata, metadata, known.Id)], cancellation);
            await db.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, cancellation);
            if ((string?)check.ExecuteScalar() != "2:1:1000") throw new InvalidOperationException("Refresh changed album history.");
            var items = Enumerable.Range(0, 128).Select(i => new SourceCacheItem("fresh-" + i, 1, 1, "{}")).ToArray();
            await db.StoreSourceCacheAsync(items, cancellation);
            check.CommandText = "SELECT count(*) FROM scan_source_cache";
            if ((long)check.ExecuteScalar()! != 262144) throw new InvalidOperationException("Initial cache maintenance failed.");
            for (var i = 0; i < 32; i++) await db.StoreSourceCacheAsync(items, cancellation);
            if ((long)check.ExecuteScalar()! != 262144) throw new InvalidOperationException("Cache updates evicted unrelated retained facts.");
            var key = new FileSnapshot("fresh-0", "fresh-0", 1, 1);
            if (!(await db.ReadSourceCacheAsync([key], cancellation)).ContainsKey(key.PathKey))
                throw new InvalidOperationException("Recent source cache was pruned.");
        }
        Console.WriteLine("MAINTENANCE v17Upgrade=true backup=true albumHistoryPreserved=true cacheRows=262144");
    }

    private static SqliteConnection Connect(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Pooling = false, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite }.ToString());
        connection.Open(); return connection;
    }
}
