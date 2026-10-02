using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal static class MetadataProbe
{
    public static async Task RunAsync(string root, CancellationToken cancellation)
    {
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "library.sqlite");
        var media = Path.Combine(directory, "media");
        ScanEntry Entry(int number, string? id = null) => new(new(Path.Combine(media, number + ".flac"),
            LibraryPath.Key(Path.Combine(media, number + ".flac")), 100, 10), ScanEntryKind.Metadata,
            new("Track " + number, "Singer", "OriginalAlbum", "OriginalArtist", 100, "flac", 44100, 16), id);
        async Task Import(LibraryDatabase database, params ScanEntry[] entries)
        {
            var job = await database.BeginScanAsync(media, false, cancellation);
            await database.CommitScanBatchAsync(job, 1, entries, cancellation);
            await database.FinishScanAsync(job, ScanStatus.Completed, entries.Length, false, null, cancellation);
        }
        MigrationFixture.ExtractV5(path, media);
        await using var db = await LibraryDatabase.OpenAsync(path, cancellation);
        var backupPath = Directory.GetFiles(directory, "library.sqlite.before-v6-*.sqlite").Single();
        using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            backup.Open(); using var probe = backup.CreateCommand();
            probe.CommandText = "PRAGMA user_version";
            Check(Convert.ToInt64(probe.ExecuteScalar()) == 5, "migration backup is not a consistent v5 snapshot");
            probe.CommandText = "SELECT rating FROM album_user_state WHERE favorite=1";
            Check(Convert.ToInt64(probe.ExecuteScalar()) == 4, "migration backup lost personal state");
        }
        await Import(db, Entry(2));
        var original = (await db.GetAlbumsAsync(new(), cancellation)).Items.Single();
        Check(original.Favorite && original.Rating == 4 && original.Id.Length == 32, "v5 migration lost favorites or stable identity");
        var id = original.Id;
        await db.SetAlbumMetadataAsync(id, AlbumMetadataField.Title, MetadataSource.Online, "OnlineAlbum", cancellation);
        await db.SetAlbumMetadataAsync(id, AlbumMetadataField.Title, MetadataSource.User, "EditedAlbum", cancellation);
        var metadata = await db.GetAlbumMetadataAsync(id, cancellation);
        Check(metadata.Title.Effective == "EditedAlbum" && metadata.Title.Source == MetadataSource.User
            && metadata.Title.File == "OriginalAlbum" && metadata.Title.Online == "OnlineAlbum", "metadata sources were overwritten or resolved incorrectly");
        var displayed = (await db.GetAlbumsAsync(new(Search: "EditedAlbum"), cancellation)).Items.Single();
        Check(displayed.Id == id && displayed.Favorite && displayed.Rating == 4, "editing changed identity or personal state");
        Check((await db.GetAlbumsAsync(new(Search: "OriginalAlbum"), cancellation)).Total == 0, "album search did not follow effective metadata");
        var tracks = (await db.GetAlbumTracksAsync(new(id), cancellation)).Items;
        Check(tracks.Count == 2 && tracks.All(x => x.Album == "EditedAlbum"), "album playback rows lost effective metadata");
        Check((await db.GetTracksAsync(new(Search: "EditedAlbum"), cancellation)).Items.Count == 2, "track search cannot find effective album metadata");
        await Import(db, Entry(1, tracks.Single(x => x.Title == "Track 1").Id), Entry(2, tracks.Single(x => x.Title == "Track 2").Id));
        Check((await db.GetAlbumMetadataAsync(id, cancellation)).Title.User == "EditedAlbum", "rescan destroyed a user override");
        await db.SetAlbumMetadataAsync(id, AlbumMetadataField.Title, MetadataSource.User, null, cancellation);
        Check((await db.GetAlbumsAsync(new(Search: "OnlineAlbum"), cancellation)).Items.Single().Id == id, "clearing user edit did not fall back to online metadata");
        await db.SetAlbumMetadataAsync(id, AlbumMetadataField.Title, MetadataSource.Online, null, cancellation);
        Check((await db.GetAlbumMetadataAsync(id, cancellation)).Title.Source == MetadataSource.File, "clearing enrichment did not restore file provenance");

        var missing = await db.BeginScanAsync(media, true, cancellation);
        await db.FinishScanAsync(missing, ScanStatus.Completed, 0, true, null, cancellation);
        Check((await db.GetAlbumsAsync(new(), cancellation)).Total == 0, "missing album remained visible");
        await Import(db, Entry(1, tracks.Single(x => x.Title == "Track 1").Id));
        var returned = (await db.GetAlbumsAsync(new(), cancellation)).Items.Single();
        Check(returned.Id == id && returned.Favorite && returned.Rating == 4, "temporary absence lost album identity or personal state");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await db.SetAlbumMetadataAsync(id, AlbumMetadataField.Title, MetadataSource.User, "Canceled", canceled.Token); throw new Exception("canceled edit was admitted"); }
        catch (OperationCanceledException) { }
        Check((await db.GetAlbumMetadataAsync(id, cancellation)).Title.Source == MetadataSource.File, "canceled edit changed metadata");
        Console.WriteLine("PASS v5 migration, stable album ID, preserved favorites, provenance/override fallback, effective FTS and playback rows, rescan/missing recovery and cancellation");
        Console.WriteLine("MetadataFixture=" + path);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
