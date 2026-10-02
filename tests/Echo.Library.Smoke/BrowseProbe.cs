using System.Diagnostics;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal static class BrowseProbe
{
    public static async Task RunAsync(string root, string baseline, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "browse.sqlite");
        await using (var db = await LibraryDatabase.OpenAsync(path, token))
        {
            var entries = Enumerable.Range(0, 34).Select(index =>
            {
                var file = Path.Combine(root, "media", index < 2 ? "A" : "B", index + ".flac");
                return new ScanEntry(new(file, LibraryPath.Key(file), 100, 1000 + index), ScanEntryKind.Metadata,
                    new("Song " + index, "Artist " + index / 2, "Album " + (index / 2).ToString("00"), "", 60 + index,
                        index % 2 == 0 ? "FLAC" : "MP3", index % 2 == 0 ? 96000 : 44100, index % 2 == 0 ? 24 : 16,
                        Year: 2000 + index / 2, Genre: index < 2 ? "Pop" : "Rock"));
            }).ToArray();
            var job = await db.BeginScanAsync(root, false, token);
            await db.CommitScanBatchAsync(job, 1, entries, token);
            await db.FinishScanAsync(job, ScanStatus.Completed, entries.Length, false, null, token);
            var rows = (await db.GetTracksAsync(new(), token)).Items.ToDictionary(row => row.Title);
            async Task Listen(string id, int track, long started, double seconds = 40)
                => await db.RecordListeningAsync(new(id, rows["Song " + track].Id, rows["Song " + track].Path, started, started + 60, seconds, EndedUnix: started + 60), token);
            await Listen("a", 0, 100); await Listen("b", 0, 200); await Listen("c", 1, 300);
            await Listen("d", 2, 500); await Listen("e", 2, 600);
            Check((await db.GetAlbumsAsync(new(Sort: AlbumSort.PlayCount, Descending: true), token)).Items[0].Title == "Album 00", "play-count order");
            Check((await db.GetAlbumsAsync(new(Sort: AlbumSort.LastPlayed, Descending: true), token)).Items[0].Title == "Album 01", "last-played order");
            var revision = (await db.GetStatsAsync(token)).Revision;
            await Listen("a", 0, 100, 45);
            Check((await db.GetStatsAsync(token)).Revision == revision, "progress upsert changed browse revision or duplicated play count");
            Check((await db.GetAlbumsAsync(new(Sort: AlbumSort.Modified, Descending: true), token)).Items[0].Title == "Album 16", "modified order");
            var filter = new LibraryFilter(FileFormat.Flac, AudioRate.Hz96000, 24, 2000, "Pop", Path.Combine(root, "media", "A"));
            Check((await db.GetAlbumsAsync(new(Filter: filter), token)).Total == 1, "combined filters");
            Check((await db.GetAlbumsAsync(new(Filter: filter with { Rate = AudioRate.Hz44100 }), token)).Total == 0, "same member intersection");
            var favored = (await db.GetAlbumsAsync(new(), token)).Items[0];
            await db.SetAlbumStateAsync(favored.Id, true, 4, token);
            Check((await db.GetAlbumsAsync(new(Filter: new(FavoriteOnly: true)), token)).Items.Single().Id == favored.Id, "album favorite membership");

            foreach (var sort in Enum.GetValues<AlbumSort>())
                foreach (var descending in new[] { false, true })
                {
                    var ids = new HashSet<string>();
                    for (var offset = 0; offset < 17; offset += 3)
                    {
                        var page = await db.GetAlbumsAsync(new(offset, 3, Sort: sort, Descending: descending, ShufflePivot: "ABCDEF0123456789"), token);
                        Check(page.Items.All(row => ids.Add(row.Id)), "duplicate pages: " + sort);
                    }
                    Check(ids.Count == 17, "missing pages: " + sort);
                }
            var random = new AlbumQuery(0, 7, Sort: AlbumSort.Random, ShufflePivot: "ABCDEF0123456789");
            Check((await db.GetAlbumsAsync(random, token)).Items.SequenceEqual((await db.GetAlbumsAsync(random, token)).Items), "unstable random pages");
            Check((await db.GetAlbumsAsync(random with { Offset = 17 }, token)).Items.Count == 0, "random wrapped beyond end");

            job = await db.BeginScanAsync(root, false, token);
            var changed = entries[0] with { File = entries[0].File with { ModifiedTicks = 2000 },
                Metadata = entries[0].Metadata! with { Album = "Moved" }, ExistingId = rows["Song 0"].Id };
            await db.CommitScanBatchAsync(job, 1, [changed], token);
            await db.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, token);
            var readOnly = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            using (readOnly)
            {
                readOnly.Open(); using var command = readOnly.CreateCommand();
                command.CommandText = "SELECT play_count FROM albums WHERE title='Moved'";
                Check((long)command.ExecuteScalar()! == 2, "history did not follow regrouped track");
                command.CommandText = "SELECT play_count FROM albums WHERE title='Album 00'";
                Check((long)command.ExecuteScalar()! == 1, "old album history did not decrement");
            }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await db.GetAlbumsAsync(new(), cancelled.Token); throw new Exception("cancellation accepted"); }
            catch (OperationCanceledException) { }
            await db.RecordListeningAsync(new("pending", rows["Song 4"].Id, rows["Song 4"].Path, 700, 750, 40), token);
            var pending = (await db.GetAlbumsAsync(new(Sort: AlbumSort.LastPlayed, Descending: true), token)).Items[0];
            Check(pending.Title == "Album 02", "recent playback should include the active session");
            using (var inspection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                inspection.Open(); using var command = inspection.CreateCommand();
                command.CommandText = "SELECT play_count FROM albums WHERE title='Album 02'";
                Check((long)command.ExecuteScalar()! == 0, "unfinished history counted as a completed play");
                await db.RecordListeningAsync(new("pending", rows["Song 4"].Id, rows["Song 4"].Path, 700, 760, 45, EndedUnix: 760), token);
                Check((long)command.ExecuteScalar()! == 1, "completion did not update play count");
                var beforeClear = (await db.GetAlbumsAsync(new(), token)).Revision;
                await db.ClearHistoryAsync(token);
                command.CommandText = "SELECT sum(play_count)+sum(last_played) FROM albums";
                Check((long)command.ExecuteScalar()! == 0, "clear left stale album listening facts");
                try { await db.GetAlbumsAsync(new(Revision: beforeClear), token); throw new Exception("clear accepted stale album snapshot"); }
                catch (LibraryChangedException) { }
            }
        }
        // Copy a fixed generated 50k-track snapshot through SQLite backup, never a live production database.
        var scalePath = Path.Combine(root, "scale.sqlite");
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = baseline, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = scalePath, Pooling = false }.ToString()))
        { source.Open(); target.Open(); source.BackupDatabase(target); }
        var timer = Stopwatch.StartNew();
        await using var scale = await LibraryDatabase.OpenAsync(scalePath, token);
        Console.WriteLine($"Generated snapshot migration: {timer.Elapsed.TotalMilliseconds:F1} ms");
        var samples = new List<double>();
        foreach (var sort in Enum.GetValues<AlbumSort>())
            foreach (var descending in new[] { false, true })
            {
                timer.Restart();
                var page = await scale.GetAlbumsAsync(new(9600, Sort: sort, Descending: descending, ShufflePivot: "ABCDEF0123456789"), token);
                Check(page.Items.Count == 200, "bounded deep page"); samples.Add(timer.Elapsed.TotalMilliseconds);
            }
        samples.Sort();
        Console.WriteLine($"Generated 50k tracks / 10k albums, 24 deep-page samples: median={samples[samples.Count / 2]:F1} ms, max={samples[^1]:F1} ms (includes count/pivot cache misses).");
        var sample = (await scale.GetAlbumsAsync(new(Limit: 1), token)).Items[0];
        var mediaRoot = Path.GetDirectoryName(Path.GetDirectoryName(sample.SamplePath));
        var filteredSamples = new List<double>();
        foreach (var field in new[] { AlbumSort.Title, AlbumSort.Modified, AlbumSort.Random })
            foreach (var depth in new int?[] { null, 24 })
            {
                timer.Restart();
                var page = await scale.GetAlbumsAsync(new(Sort: field, Filter: new(FileFormat.Flac, AudioRate.Hz48000, depth, Folder: mediaRoot), ShufflePivot: "1234567890ABCDEF"), token);
                Check(page.Items.Count == 200, "combined-filter bounded scale page"); filteredSamples.Add(timer.Elapsed.TotalMilliseconds);
            }
        filteredSamples.Sort();
        Console.WriteLine($"Six combined format/rate/depth/folder first-page samples: median={filteredSamples[3]:F1} ms, max={filteredSamples[^1]:F1} ms.");
        Console.WriteLine("Browse: sort pagination, seeded random wrap, listening idempotence, regrouping, combined filters, album favorites and cancellation passed.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
