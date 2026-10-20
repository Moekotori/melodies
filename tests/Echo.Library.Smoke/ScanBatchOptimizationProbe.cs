using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal static class ScanBatchOptimizationProbe
{
    public static async Task RunAsync(string root, string previousDatabase, string previous23Database, CancellationToken token)
    {
        Directory.CreateDirectory(root); var checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        var path = Path.Combine(root, "batch.sqlite"); var media = Path.Combine(root, "media");
        ScanEntry Entry(int index)
        {
            var name = Path.Combine(media, $"track-{index:D4}.flac");
            return new(new(name, LibraryPath.Key(name), 100, 123), ScanEntryKind.Metadata,
                new("Track " + index, index == 259 ? "B" : "A", "Album", "", 10, "FLAC", 48000, 24, Year: 2024));
        }
        var entries = Enumerable.Range(0, 260).Select(Entry).ToArray();
        await using (var db = await LibraryDatabase.OpenAsync(path, token))
        {
            var job = await db.BeginScanAsync(media, false, token);
            await db.CommitScanBatchAsync(job, 1, entries[..256], token);
            await db.CommitScanBatchAsync(job, 2, entries[256..], token);
            await db.FinishScanAsync(job, ScanStatus.Completed, entries.Length, false, null, token);
            var album = (await db.GetAlbumsAsync(new(), token)).Items.Single();
            Check(album.TrackCount == 260 && album.DurationSeconds == 2600 && album.Artist == "Various Artists", "batch album compilation facts");
            var artist = (await db.GetArtistsAsync(new(Grouping: ArtistGrouping.AlbumArtist), token)).Items.Single();
            Check(artist.Name == "Various Artists" && artist.TrackCount == 260, "paged shared-artist reconciliation");
            var known = await db.FindKnownAsync([entries[0].File, entries[259].File], token);
            job = await db.BeginScanAsync(media, false, token);
            var collision = entries[0] with { File = entries[0].File with { Path = entries[0].File.Path.ToUpperInvariant() }, ExistingId = known[entries[0].File.PathKey].Id };
            try
            {
                await db.CommitScanBatchAsync(job, 1, [Entry(500), collision], token);
                throw new Exception("Collision batch was accepted");
            }
            catch (MetadataReadException error) when (error.Code == "path_case_identity_required") { checks++; }
            Check((await db.GetStatsAsync(token)).Tracks == 260 && (await db.GetTracksAsync(new(Search: "Track 500"), token)).Items.Count == 0,
                "rollback leaves no tracks/search or aggregate fragments");
            var repaired = entries[259] with { Metadata = entries[259].Metadata! with { Artist = "A" }, ExistingId = known[entries[259].File.PathKey].Id };
            var receipt = await db.CommitScanBatchAsync(job, 1, [repaired], token);
            Check(receipt == await db.CommitScanBatchAsync(job, 1, [repaired], token), "same batch receipt after source preparation");
            Check((await db.GetArtistsAsync(new(Grouping: ArtistGrouping.AlbumArtist), token)).Items.Single() is { Name: "A", TrackCount: 260 },
                "compilation becoming a single artist repairs all pages");
            var firstId = known[entries[0].File.PathKey].Id;
            await db.SaveTrackTagsAsync(firstId, new("Edited", "User artist", "User album", "", null, null, 2025, "Jazz"), token);
            Check((await db.GetAlbumsAsync(new(Search: "User album"), token)).Items.Single() is { Artist: "User artist", TrackCount: 1 },
                "ordinary writes refresh immediately after failed scan transaction");
            await db.CommitScanBatchAsync(job, 2, [entries[0] with { ExistingId = firstId }], token);
            Check((await db.GetAlbumsAsync(new(Search: "User album"), token)).Items.Single() is { Artist: "User artist", TrackCount: 1 }, "user tags survive deferred metadata reimport");
            var total = (await db.GetAlbumsAsync(new(), token)).Items.Sum(item => item.TrackCount);
            Check(total == 260, "moving tracks preserves total aggregate count");
            await db.FinishScanAsync(job, ScanStatus.Completed, 2, false, null, token);
        }
        using (var connection = Open(path, true))
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*) FROM scan_album_dirty";
            Check((long)command.ExecuteScalar()! == 0, "dirty set empty after commit/rollback");
            command.CommandText = "EXPLAIN QUERY PLAN SELECT DISTINCT artist_id FROM artist_tracks WHERE grouping=1 AND album_id='album' LIMIT 65";
            using var rows = command.ExecuteReader(); var indexed = false;
            while (rows.Read()) indexed |= rows.GetString(3).Contains("artist_tracks_album_members", StringComparison.Ordinal);
            Check(indexed, "album artist query uses covering index");
        }
        var upgradePath = Path.Combine(root, "upgrade.sqlite");
        using (var previous = Open(previousDatabase, true))
        using (var target = Open(upgradePath, false)) previous.BackupDatabase(target);
        long before;
        using (var connection = Open(upgradePath, false))
        {
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version";
            Check((long)command.ExecuteScalar()! == 22, "upgrade uses an actual pre-change v22 database");
            command.CommandText = "UPDATE tracks SET favorite=1 WHERE rowid=(SELECT min(rowid) FROM tracks); SELECT count(*) FROM tracks";
            before = (long)command.ExecuteScalar()!;
        }
        await using (var migrated = await LibraryDatabase.OpenAsync(upgradePath, token))
        {
            Check((await migrated.GetStatsAsync(token)).Tracks == before, "v22 upgrade preserves library");
            Check((await migrated.GetTracksAsync(new(Filter: new(FavoriteOnly: true)), token)).Items.Count == 1, "upgrade preserves personal state");
        }
        var backupPath = Directory.GetFiles(root, "upgrade.sqlite.before-v23-*.sqlite").Single();
        using (var backup = Open(backupPath, true))
        {
            using var command = backup.CreateCommand(); command.CommandText = "PRAGMA user_version";
            Check((long)command.ExecuteScalar()! == 22, "consistent pre-migration backup");
        }
        var repairPath = Path.Combine(root, "repair-v23.sqlite");
        using (var previous = Open(previous23Database, true))
        using (var target = Open(repairPath, false)) previous.BackupDatabase(target);
        await using (var repaired = await LibraryDatabase.OpenAsync(repairPath, token))
        {
            var track = (await repaired.GetTracksAsync(new(1), token)).Items.Single();
            var name = new FileSnapshot(track.Path, LibraryPath.Key(track.Path), 100, 123);
            var job = await repaired.BeginScanAsync(Path.GetDirectoryName(track.Path)!, false, token);
            await repaired.CommitScanBatchAsync(job, 1, [new(name, ScanEntryKind.Metadata,
                new(track.Title, "Changed", track.Album, "", 20, "PCM", 48000, 16), track.Id)], token);
            await repaired.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, token);
            Check((await repaired.GetStatsAsync(token)).Tracks == 2048, "v23 trigger repair accepts repeated dirty-album UPSERT");
        }
        Check(Directory.GetFiles(root, "repair-v23.sqlite.before-v24-*.sqlite").Length == 1, "v23 repair backup");
        var boundaries = await Scenarios.RunAsync(Path.Combine(root, "scan-boundaries"), null, token);
        await LargeLibraryRecoveryProbe.RunAsync(Path.Combine(root, "recovery"), token);
        Console.WriteLine("SCAN_BOUNDARIES_PASSED checks=" + boundaries);
        Console.WriteLine("SCAN_BATCH_OPTIMIZATION_PASSED checks=" + checks);
    }

    private static SqliteConnection Open(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
}
