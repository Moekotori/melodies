using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal static partial class ArtistGroupingSmoke
{
    public static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = stop.Token;
        var path = Path.Combine(root, "grouping.sqlite");
        string jointId, albumId;
        long revision;
        await using (var database = await LibraryDatabase.OpenAsync(path, token))
        {
            Check((await database.GetArtistsAsync(new(), token)).Revision == 0, "A new empty library keeps its initial revision.");
            var media = Path.Combine(root, "media");
            ScanEntry Entry(string folder, string title, string credit, string albumCredit) =>
                new(new(Path.Combine(media, folder, title + ".flac"), LibraryPath.Key(Path.Combine(media, folder, title + ".flac")), 100, 100),
                    ScanEntryKind.Metadata, new(title, credit, folder, albumCredit, 60, "FLAC", 44100, 16));
            var job = await database.BeginScanAsync(media, false, token);
            ScanEntry[] entries = [Entry("Joint", "One", "Alpha; Beta", "Alpha; Beta"),
                Entry("Joint", "Two", "Alpha; Beta", "Alpha; Beta"),
                Entry("Compilation", "Three", "Alpha", ""), Entry("Compilation", "Four", "Beta", ""),
                Entry("Band", "Five", "AC/DC", "AC/DC"),
                Entry("Band2", "Six", "Earth, Wind & Fire", "Earth, Wind & Fire"),
                Entry("Fallback", "Seven", "Alpha; Beta", "")];
            await database.CommitScanBatchAsync(job, 1, entries, token);
            await database.FinishScanAsync(job, ScanStatus.Completed, entries.Length, false, null, token);
            await CheckGroupsAsync(database, token);
            var tracks = (await database.GetTracksAsync(new(), token)).Items;
            jointId = tracks.Single(track => track.Title == "One").Id;
            var joint = (await database.GetArtistsAsync(new(Search: "Alpha; Beta", Grouping: ArtistGrouping.AlbumArtist), token)).Items.Single();
            albumId = (await database.GetArtistAlbumsAsync(joint.Id, joint.Grouping, new(), token)).Items.First().Id;
            await database.SetTrackFavoriteAsync(jointId, true, token);
            await database.SetAlbumStateAsync(albumId, true, 4, token);
            revision = (await database.GetArtistsAsync(new(), token)).Revision;
        }
        // Reproduce the old split album-artist index in an offline test database.
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM artist_tracks WHERE grouping=1 AND artist_id='alpha; beta';
                INSERT INTO artist_tracks
                  SELECT 1,artist_id,name,track_id,album_id,duration,added FROM artist_tracks
                  WHERE grouping=0 AND track_id IN(SELECT id FROM tracks WHERE artist='Alpha; Beta');
                DROP TRIGGER credit_track_delete;
                DROP TRIGGER person_credit_delete;
                DROP TABLE person_credits;
                DROP TABLE credit_people;
                DROP TABLE track_credit_tags;
                DROP TABLE artist_delimiters;
                DROP TABLE artist_import_state;
                PRAGMA user_version=24;
                """;
            command.ExecuteNonQuery();
        }
        await using (var database = await LibraryDatabase.OpenAsync(path, token))
        {
            await CheckGroupsAsync(database, token);
            var page = await database.GetArtistsAsync(new(), token);
            Check(page.Revision == revision + 1, "Migration invalidates stale artist pages once.");
            var tracks = (await database.GetTracksAsync(new(), token)).Items;
            Check(tracks.Count == 7 && tracks.Single(track => track.Id == jointId).Favorite, "Migration preserves track identity and favorites.");
            var albums = (await database.GetAlbumsAsync(new(), token)).Items;
            Check(albums.Single(album => album.Id == albumId) is { Favorite: true, Rating: 4 }, "Migration preserves album identity and personal state.");
        }
        Check(Directory.GetFiles(root, "*.before-v27-*.sqlite").Length == 1, "Existing libraries get a migration backup.");
        await using (var database = await LibraryDatabase.OpenAsync(path, token))
            Check((await database.GetArtistsAsync(new(), token)).Revision == revision + 1, "Reopening does not repeat the migration.");
        Console.WriteLine("PASS ECHO artist grouping: split song credits, literal album credits, compilation/fallback/band identities, v24 migration, preserved personal state and idempotent reopen.");
    }
    private static async Task CheckGroupsAsync(IArtistLibrary library, CancellationToken token)
    {
        var songs = await library.GetArtistsAsync(new(), token);
        Check(songs.Items.Single(row => row.Name == "Alpha").TrackCount == 4
            && songs.Items.Single(row => row.Name == "Beta").TrackCount == 4, "Song artists split explicit collaboration credits.");
        Check(songs.Items.Any(row => row.Name == "AC/DC") && songs.Items.Any(row => row.Name == "Earth, Wind & Fire"), "Ambiguous band names stay intact.");
        var albums = await library.GetArtistsAsync(new(Grouping: ArtistGrouping.AlbumArtist), token);
        var joint = albums.Items.Single(row => row.Name == "Alpha; Beta");
        Check(joint.TrackCount == 3 && joint.AlbumCount == 2, "Album artists retain complete credits, including untagged album fallback.");
        Check(albums.Items.All(row => row.Name is not ("Alpha" or "Beta")), "Compilation performers do not become album owners.");
        Check(albums.Items.Single(row => row.Name == "Various Artists").TrackCount == 2, "Mixed untagged performers use the shared compilation identity.");
        Check((await library.GetArtistTracksAsync(new(joint.Id, joint.Grouping), token)).Items.Count == 3, "Album-artist detail uses the same membership.");
        Check((await library.GetArtistAlbumsAsync(joint.Id, joint.Grouping, new(), token)).Total == 2, "Album-artist album navigation uses the same membership.");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
