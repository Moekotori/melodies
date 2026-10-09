using Echo.Contracts.Library;
using Echo.Contracts.Remote;
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

        // Rewind the album key to the previous generated column, then let schema 20 regroup it.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT type,name,sql FROM sqlite_master WHERE type IN ('trigger','index') AND sql LIKE '%album_key%'";
            var dependents = new List<(string Type, string Name, string Sql)>();
            using (var reader = command.ExecuteReader())
                while (reader.Read()) dependents.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            foreach (var item in dependents)
            {
                command.CommandText = (item.Type == "index" ? "DROP INDEX " : "DROP TRIGGER ")
                    + "\"" + item.Name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
                command.ExecuteNonQuery();
            }
            command.CommandText = """
                ALTER TABLE tracks DROP COLUMN album_key;
                ALTER TABLE tracks ADD COLUMN album_key TEXT GENERATED ALWAYS AS
                  (printf('%d:%s%d:%s',length(trim(album)),trim(album),length(album_credit),album_credit)) VIRTUAL;
                PRAGMA user_version=18;
                """;
            command.ExecuteNonQuery();
            foreach (var item in dependents)
            {
                command.CommandText = item.Sql;
                command.ExecuteNonQuery();
            }
        }
        await using (var migrated = await LibraryDatabase.OpenAsync(path, cancellation))
        {
            var album = (await migrated.GetAlbumsAsync(new(), cancellation)).Items.Single();
            Require(album.TrackCount == 2 && album.DurationSeconds == 180 && album.Year == 2024, "album regrouping preserved the aggregate");
        }
        await using (var remote = await LibraryDatabase.OpenAsync(Path.Combine(root, "remote.sqlite"), cancellation))
        {
            var receipt = await remote.WriteAsync([
                new RemoteIndexTrack("box", "/Album/CD1/a.flac", "甲", "歌手甲", "网盘套装", 10, 10, 3, "FLAC", "同一人"),
                new RemoteIndexTrack("box", "/Album/CD2/b.flac", "乙", "歌手乙", "网盘套装", 10, 10, 4, "FLAC", "同一人"),
                new RemoteIndexTrack("box", "/Album/Other/c.flac", "丙", "歌手丙", "网盘套装", 10, 10, 5, "FLAC", "同一人"),
                new RemoteIndexTrack("box", "/Elsewhere/d.flac", "丁", "歌手丁", "网盘套装", 10, 10, 6, "FLAC", "同一人")
            ], "sync-1", cancellation);
            var boxed = await remote.GetAlbumsAsync(new(Search: "网盘套装"), cancellation);
            Require(receipt.Inserted == 4 && boxed.Total == 3 && boxed.Items.Count(album => album.TrackCount == 2) == 1,
                "remote disc folders stay together and do not collapse into one album");
        }
        var baseline = await Scenarios.RunAsync(Path.Combine(root, "scan-baseline"), null, cancellation);
        Console.WriteLine($"Album maintenance/missing/restore/regrouping passed; existing scan boundary checks={baseline}.");
        await AlbumArtworkProbe.RunAsync(Path.Combine(root, "artwork"), cancellation);
    }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
