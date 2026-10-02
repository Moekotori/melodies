using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal static class AlbumMaintenanceProbe
{
    public static async Task RunAsync(string root, CancellationToken cancellation)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "maintenance.sqlite");
        var media = Path.Combine(root, "media");
        await using (var db = await LibraryDatabase.OpenAsync(path, cancellation))
        {
            var job = await db.BeginScanAsync(media, false, cancellation);
            ScanEntry Entry(int index, int year, double duration, string credit = "Artist")
            {
                var file = Path.Combine(media, index + ".flac");
                return new(new(file, LibraryPath.Key(file), 100, 10), ScanEntryKind.Metadata,
                    new("Track " + index, "Singer " + index, "Album", credit, duration, "FLAC", 44100, 16, Year: year));
            }
            await db.CommitScanBatchAsync(job, 1, [Entry(0, 2025, 200), Entry(1, 2024, 100), Entry(2, 2023, 50, "Other Artist")], cancellation);
            await db.FinishScanAsync(job, ScanStatus.Completed, 3, false, null, cancellation);
            var albums = await db.GetAlbumsAsync(new(), cancellation);
            Require(albums.Total == 2, "same title different credit stays separate");
            var album = albums.Items.Single(a => a.Artist == "Artist");
            Require(album.TrackCount == 2 && album.Year == 2025 && album.DurationSeconds == 300, "initial aggregate");
            var tracks = await db.GetAlbumTracksAsync(new(album.Id), cancellation);
            await db.SetAlbumStateAsync(album.Id, true, 4, cancellation);
            await db.SetTrackFavoriteAsync(tracks.Items[0].Id, true, cancellation);
            var saved = (await db.GetAlbumsAsync(new(), cancellation)).Items.Single(a => a.Id == album.Id);
            Require(saved.Favorite && saved.Rating == 4 && (await db.GetAlbumTracksAsync(new(album.Id), cancellation)).Items[0].Favorite,
                "personal state readback through single owner");
            var changing = tracks.Items.Single(t => t.Title == "Track 0");
            job = await db.BeginScanAsync(media, false, cancellation);
            var entry = Entry(0, 2020, 80) with { ExistingId = changing.Id };
            await db.CommitScanBatchAsync(job, 1, [entry], cancellation);
            await db.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, cancellation);
            album = (await db.GetAlbumsAsync(new(), cancellation)).Items.Single(a => a.Artist == "Artist");
            Require(album.TrackCount == 2 && album.Year == 2024 && album.DurationSeconds == 180, "same album metadata subtraction and year correction");

            // Mark every absent track missing through the real single writer.
            job = await db.BeginScanAsync(media, true, cancellation);
            var keep = Entry(1, 2024, 100);
            var known = await db.FindKnownAsync([keep.File], cancellation);
            await db.CommitScanBatchAsync(job, 1, [keep with { Kind = ScanEntryKind.Unchanged, Metadata = null, ExistingId = known[keep.File.PathKey].Id }], cancellation);
            await db.FinishScanAsync(job, ScanStatus.Completed, 1, true, null, cancellation);
            album = (await db.GetAlbumsAsync(new(), cancellation)).Items.Single();
            Require(album.TrackCount == 1 && album.Year == 2024 && album.DurationSeconds == 100, "missing reconciliation removes empty album");
            job = await db.BeginScanAsync(media, false, cancellation);
            await db.CommitScanBatchAsync(job, 1, [entry], cancellation);
            await db.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, cancellation);
            Require((await db.GetAlbumsAsync(new(), cancellation)).Items.Single().TrackCount == 2, "returning missing track restored");
        }

        // Recreate a genuine v1 snapshot, then let the only schema owner migrate it.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TRIGGER tracks_album_insert; DROP TRIGGER tracks_album_update; DROP TRIGGER tracks_album_delete;
                DROP TRIGGER albums_fts_insert; DROP TRIGGER albums_fts_delete;
                DROP TABLE albums_fts; DROP TABLE albums; DROP TABLE album_user_state;
                DROP INDEX tracks_album; DROP INDEX tracks_album_year;
                ALTER TABLE tracks DROP COLUMN album_key; ALTER TABLE tracks DROP COLUMN album_credit;
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery();
        }
        await using (var migrated = await LibraryDatabase.OpenAsync(path, cancellation))
        {
            var album = (await migrated.GetAlbumsAsync(new(), cancellation)).Items.Single();
            Require(album.TrackCount == 2 && album.DurationSeconds == 180 && album.Year == 2024, "v1 backfill parity");
        }
        var baseline = await Scenarios.RunAsync(Path.Combine(root, "scan-baseline"), null, cancellation);
        Console.WriteLine($"Album maintenance/missing/restore/v1 migration passed; existing scan boundary checks={baseline}.");
        await AlbumArtworkProbe.RunAsync(Path.Combine(root, "artwork"), cancellation);
    }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
