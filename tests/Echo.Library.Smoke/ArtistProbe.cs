using System.Diagnostics;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal static class ArtistProbe
{
    public static async Task RunAsync(string root, CancellationToken cancellation)
    {
        Directory.CreateDirectory(root);
        var media = Path.Combine(root, "media"); var path = Path.Combine(root, "artists.sqlite");
        string[] names = ["Aimer", "Aurora", "Björk", "Daft Punk", "Lana Del Rey", "Nujabes", "Radiohead", "坂本龍一", "宇多田ヒカル", "林俊杰", "陈奕迅", "椎名林檎", "AC/DC", "Earth, Wind & Fire", "Cigarettes After Sex", "The xx"];
        await using (var database = await LibraryDatabase.OpenAsync(path, cancellation))
        {
            var job = await database.BeginScanAsync(media, false, cancellation); long sequence = 0;
            var timer = Stopwatch.StartNew();
            for (var offset = 0; offset < 50_000; offset += 200)
            {
                var entries = Enumerable.Range(offset, 200).Select(index =>
                {
                    var group = index / 5; var name = group < names.Length ? names[group] : $"~ Artist {group:00000}";
                    var file = Path.Combine(media, $"artist-{group:00000}", $"track-{index:00000}.flac");
                    return new ScanEntry(new(file, LibraryPath.Key(file), 100, 123), ScanEntryKind.Metadata,
                        new($"Song {index % 5 + 1}", group == 0 ? "Aimer feat. Aurora" : name,
                            $"Record {group:00000}", name, 180, "FLAC", 48000, 24, TrackNumber: index % 5 + 1, Year: 2020));
                }).ToArray();
                await database.CommitScanBatchAsync(job, ++sequence, entries, cancellation);
            }
            await database.FinishScanAsync(job, ScanStatus.Completed, 50000, false, null, cancellation);
            var insertMs = timer.Elapsed.TotalMilliseconds;
            var page = await database.GetArtistsAsync(new(), cancellation);
            Require(page.Total == 10000 && page.Items.Count == 200, "artist page count");
            Require(page.Items.Any(a => a.Name == "AC/DC") && page.Items.Any(a => a.Name == "Earth, Wind & Fire"), "ambiguous band names preserved");
            var aurora = (await database.GetArtistsAsync(new(Search: "Aurora"), cancellation)).Items.Single();
            Require(aurora.TrackCount == 10 && aurora.AlbumCount == 2 && aurora.DurationSeconds == 1800, "featured participants distinct album count");
            var albumArtist = (await database.GetArtistsAsync(new(Search: "Aurora", Grouping: ArtistGrouping.AlbumArtist), cancellation)).Items.Single();
            Require(albumArtist.TrackCount == 5 && albumArtist.AlbumCount == 1, "album artist scope");
            var albums = await database.GetArtistAlbumsAsync(aurora.Id, aurora.Grouping, new(), cancellation);
            Require(albums.Total == 2 && albums.Items.Count == 2, "artist album source");
            Require((await database.GetAlbumTracksAsync(new(albums.Items[0].Id), cancellation)).Items.Count == 5, "nested album lookup");
            var songs = await database.GetArtistTracksAsync(new(aurora.Id, aurora.Grouping), cancellation);
            Require(songs.Items.Count == 10, "artist track lookup");
            var samples = new List<double>();
            foreach (var sort in Enum.GetValues<ArtistSort>())
                foreach (var descending in new[] { false, true })
                {
                    timer.Restart(); var deep = await database.GetArtistsAsync(new(9600, Sort: sort, Descending: descending), cancellation);
                    Require(deep.Items.Count == 200, "deep artist page"); samples.Add(timer.Elapsed.TotalMilliseconds);
                }
            var track = songs.Items.First(t => t.Artist == "Aimer feat. Aurora");
            job = await database.BeginScanAsync(media, false, cancellation);
            var moved = new ScanEntry(new(track.Path, LibraryPath.Key(track.Path), 200, 124), ScanEntryKind.Metadata,
                new("Moved", "Moved Artist", "Record 00000", "Aimer", 90, "FLAC", 48000, 24), track.Id);
            await database.CommitScanBatchAsync(job, 1, [moved], cancellation);
            await database.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, cancellation);
            Require((await database.GetArtistsAsync(new(Search: "Aurora"), cancellation)).Items.Single().TrackCount == 9, "metadata recredit subtraction");
            Require((await database.GetArtistsAsync(new(Search: "Moved"), cancellation)).Items.Single().TrackCount == 1, "metadata recredit addition");
            try { await database.GetArtistsAsync(new(Revision: page.Revision), cancellation); throw new Exception("stale revision accepted"); }
            catch (LibraryChangedException) { }
            using var stopped = new CancellationTokenSource(); stopped.Cancel();
            try { await database.GetArtistsAsync(new(), stopped.Token); throw new Exception("cancellation ignored"); }
            catch (OperationCanceledException) { }
            samples.Sort();
            Console.WriteLine($"Artists: 50,000 tracks / 10,000 initial artists; insert={insertMs:F1}ms; deep samples={samples.Count}; median={samples[samples.Count / 2]:F2}ms; max={samples[^1]:F2}ms; credits, bands, two grouping modes, nested albums, update, revision and cancellation passed.");
        }
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            foreach (var column in new[] { "sort_key", "track_count", "album_count", "added" })
            {
                command.CommandText = $"EXPLAIN QUERY PLAN SELECT id FROM artists WHERE grouping=0 ORDER BY {column},id LIMIT 200 OFFSET 9600";
                using var reader = command.ExecuteReader(); var indexed = false;
                while (reader.Read()) indexed |= reader.GetString(3).Contains("USING COVERING INDEX artists_", StringComparison.Ordinal);
                Require(indexed, "artist index " + column);
            }
        }
        await MaintenanceAsync(Path.Combine(root, "maintenance"), cancellation);
    }
    private static async Task MaintenanceAsync(string root, CancellationToken cancellation)
    {
        var media = Path.Combine(root, "media"); var path = Path.Combine(root, "artists.sqlite");
        await using var database = await LibraryDatabase.OpenAsync(path, cancellation);
        var job = await database.BeginScanAsync(media, false, cancellation);
        var file = Path.Combine(media, "track.flac");
        var entry = new ScanEntry(new(file, LibraryPath.Key(file), 100, 100), ScanEntryKind.Metadata,
            new("Track", "Alpha; Beta; Alpha", "Album", "Alpha", 60, "FLAC", 44100, 16));
        await database.CommitScanBatchAsync(job, 1, [entry], cancellation);
        await database.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, cancellation);
        Require((await database.GetArtistsAsync(new(), cancellation)).Items.All(a => a.TrackCount == 1), "same participant counted once");
        job = await database.BeginScanAsync(media, true, cancellation);
        await database.FinishScanAsync(job, ScanStatus.Completed, 0, true, null, cancellation);
        Require((await database.GetArtistsAsync(new(), cancellation)).Total == 0, "missing removes empty artist");
        job = await database.BeginScanAsync(media, false, cancellation);
        var known = await database.FindKnownAsync([entry.File], cancellation);
        await database.CommitScanBatchAsync(job, 1, [entry with { ExistingId = known[entry.File.PathKey].Id }], cancellation);
        await database.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, cancellation);
        Require((await database.GetArtistsAsync(new(), cancellation)).Total == 2, "missing artist restored");
        Console.WriteLine("Artist maintenance: duplicate credits, missing cleanup and restoration passed.");
    }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException("Artist probe: " + reason); }
}
