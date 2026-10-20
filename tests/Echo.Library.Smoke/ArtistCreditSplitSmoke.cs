using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;
using Microsoft.Data.Sqlite;

internal static class ArtistCreditSplitSmoke
{
    private static readonly (string Credit, string[] Names)[] Cases =
    [
        ("Alpha feat. Beta", ["Alpha feat. Beta"]),
        ("Alpha FT.Beta", ["Alpha FT.Beta"]),
        ("Alpha featuring Beta with Gamma", ["Alpha featuring Beta with Gamma"]),
        ("Alpha (feat. Beta)", ["Alpha (feat. Beta)"]),
        ("Alpha[ft. Beta] feat. Gamma", ["Alpha[ft. Beta] feat. Gamma"]),
        ("Alpha (feat. Beta (DJ))", ["Alpha (feat. Beta (DJ))"]),
        ("Alpha (Alias / Original); Beta", ["Alpha (Alias / Original)", "Beta"]),
        ("Ａｌｐｈａ；Ｂｅｔａ／Ｇａｍｍａ", ["Alpha", "Beta/Gamma"]),
        ("Alpha\u200b ｆｅａｔ． Beta", ["Alpha feat. Beta"]),
        ("Alpha; ALPHA; Ａｌｐｈａ; Beta", ["Alpha", "Beta"]),
        ("周杰伦/林俊杰 × 宇多田ヒカル", ["周杰伦/林俊杰 × 宇多田ヒカル"]),
        ("ASCA,ReoNa", ["ASCA,ReoNa"]),
        ("Foo/Bar/Baz", ["Foo/Bar/Baz"]),
        ("Alpha x Beta", ["Alpha x Beta"]),
        ("MAN WITH A MISSION", ["MAN WITH A MISSION"]),
        ("Brother x Sister", ["Brother x Sister"]),
        ("Tyler,the Creator", ["Tyler,the Creator"]),
        ("Alpha\0Beta; Gamma\0ALPHA", ["Alpha", "Beta", "Gamma"]),
        ("\0\0", []),
        ("AC/DC", ["AC/DC"]),
        ("AC/DC; Guest", ["AC/DC", "Guest"]),
        ("AC/DC feat. Guest", ["AC/DC feat. Guest"]),
        ("Guest / AC/DC / Another", ["Guest / AC/DC / Another"]),
        ("Artist 1 / 2 / 3 feat. Guest", ["Artist 1 / 2 / 3 feat. Guest"]),
        ("１２／３４; Guest", ["12/34", "Guest"]),
        ("Earth, Wind & Fire", ["Earth, Wind & Fire"]),
        ("Tyler, the Creator feat. Guest", ["Tyler, the Creator feat. Guest"]),
        ("Simon ＆ Garfunkel", ["Simon & Garfunkel"]),
        ("Florence + The Machine", ["Florence + The Machine"]),
        ("X Ambassadors", ["X Ambassadors"]),
        ("The xx", ["The xx"]),
        ("Foxtrot; Withers", ["Foxtrot", "Withers"]),
        ("; Alpha ;; Beta ;", ["Alpha", "Beta"]),
        ("   ", []),
    ];
    public static async Task RunAsync(string root)
    {
        foreach (var (credit, names) in Cases)
            Check(ArtistCredits.Split(credit).SequenceEqual(names), $"Credit '{credit}': {string.Join(" | ", ArtistCredits.Split(credit))}");
        var maximum = Enumerable.Range(0, 64).Select(index => "Artist" + index).ToArray();
        Check(ArtistCredits.Split(string.Join(';', maximum)).SequenceEqual(maximum), "All 64 participants survive.");
        var dense = string.Join(';', maximum.Append("Artist64"));
        Check(ArtistCredits.Split(dense).SequenceEqual([dense]), "Dense credits stay whole instead of truncating people.");
        var nested = "Guest";
        for (var index = 0; index < 10; index++) nested = "Alpha (feat. " + nested + ")";
        Check(ArtistCredits.Split(nested).SequenceEqual([nested]), "Bracket text does not invent participant identities.");
        FileTagValues(root);
        await DatabaseAsync(root);
        Console.WriteLine($"PASS artist credit splitting: {Cases.Length} conservative delimiter cases, repeated FLAC tags, NUL boundaries, bounded participants, scan updates and v26 index migration.");
    }
    private static async Task DatabaseAsync(string root)
    {
        Directory.CreateDirectory(root);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var token = stop.Token;
        var path = Path.Combine(root, "credits.sqlite"); var file = Path.Combine(root, "media", "one.flac");
        var metadata = new TrackMetadata("One", "MAN WITH A MISSION; Guest", "Joint", "MAN WITH A MISSION; Guest", 60, "FLAC", 44100, 16);
        var entry = new ScanEntry(new(file, LibraryPath.Key(file), 100, 100), ScanEntryKind.Metadata, metadata);
        string id; long revision;
        await using (var database = await LibraryDatabase.OpenAsync(path, token))
        {
            await ScanAsync(database, entry);
            var songs = await database.GetArtistsAsync(new(), token);
            Check(songs.Items.Select(row => row.Name).Order().SequenceEqual(new[] { "MAN WITH A MISSION", "Guest" }.Order()), "Scan respects explicit delimiters and preserves a band name containing WITH.");
            id = (await database.GetTracksAsync(new(), token)).Items.Single().Id;
            await database.SetTrackFavoriteAsync(id, true, token);
            revision = (await database.GetArtistsAsync(new(), token)).Revision;
        }
        // Recreate v26's inferred WITH fragments; source tags and album credits stay authoritative.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM artist_tracks WHERE grouping=0;
                INSERT INTO artist_tracks
                  SELECT 0,'man','MAN',id,album_key,duration,updated_at FROM tracks
                  UNION ALL SELECT 0,'a mission','A MISSION',id,album_key,duration,updated_at FROM tracks
                  UNION ALL SELECT 0,'guest','Guest',id,album_key,duration,updated_at FROM tracks;
                DROP TRIGGER credit_track_delete;
                DROP TRIGGER person_credit_delete;
                DROP TABLE person_credits;
                DROP TABLE credit_people;
                DROP TABLE track_credit_tags;
                DROP TABLE artist_delimiters;
                DROP TABLE artist_import_state;
                PRAGMA user_version=26;
                """;
            command.ExecuteNonQuery();
        }
        await using (var database = await LibraryDatabase.OpenAsync(path, token))
        {
            var songs = await database.GetArtistsAsync(new(), token);
            Check(songs.Revision == revision + 1 && songs.Items.Any(row => row.Name == "MAN WITH A MISSION")
                && songs.Items.All(row => row.Name is not ("MAN" or "A MISSION")), "Existing song credits rebuild without inferred fragments.");
            Check((await database.GetTracksAsync(new(), token)).Items.Single() is { Favorite: true } track && track.Id == id && track.Artist == metadata.Artist,
                "Track identity, favorite and original credit survive reindexing.");
            var albums = await database.GetArtistsAsync(new(Grouping: ArtistGrouping.AlbumArtist), token);
            Check(albums.Items.Single().Name == metadata.AlbumArtist, "Song reindexing keeps the literal album credit.");
            await ScanAsync(database, entry with { File = entry.File with { ModifiedTicks = 101 }, ExistingId = id,
                Metadata = metadata with { Artist = "Alpha; Beta; ALPHA" } });
            songs = await database.GetArtistsAsync(new(), token);
            Check(songs.Items.Count == 2 && songs.Items.All(row => row.TrackCount == 1)
                && songs.Items.Any(row => row.Name == "Alpha") && songs.Items.Any(row => row.Name == "Beta"),
                "A rescan removes old credits and counts repeated participants once.");
        }
        Check(Directory.GetFiles(root, "*.before-v27-*.sqlite").Length == 1, "v26 upgrade creates one consistent backup.");
        async Task ScanAsync(LibraryDatabase database, ScanEntry item)
        {
            var job = await database.BeginScanAsync(Path.GetDirectoryName(file)!, false, token);
            await database.CommitScanBatchAsync(job, 1, [item], token);
            await database.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, token);
        }
    }
    private static void FileTagValues(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "multi-value.flac");
        AudioFixture.Flac(path, ["ARTIST=MAN WITH A MISSION", "ARTIST=Tyler,the Creator", "ARTIST=Alpha\0Beta",
            "ALBUM_ARTIST=Joint Credit", "ALBUM=Multi-value"]);
        var original = File.ReadAllBytes(path);
        var metadata = FlacMetadataReader.Read(AudioFixture.Snapshot(path), CancellationToken.None);
        Check(ArtistCredits.Split(metadata.Artist).SequenceEqual(["MAN WITH A MISSION", "Tyler,the Creator", "Alpha", "Beta"]),
            "Repeated file tags and embedded NULs preserve explicit participants without splitting names.");
        Check(metadata.AlbumArtist == "Joint Credit", "A documented album-artist tag alias is recognized.");
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(original), "Import never rewrites source tags.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
