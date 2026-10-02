using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal static class SearchProbe
{
    public static async Task RunAsync(string root, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "search.sqlite");
        // Unique fixture directory per invocation; production databases are never consulted.
        await using var db = await LibraryDatabase.OpenAsync(path, token);
        var job = await db.BeginScanAsync(root, false, token);
        var entries = new List<ScanEntry>();
        for (var i = 0; i < 430; i++)
        {
            var file = Path.Combine(root, i < 2 ? "A" : "B", $"track{i:000}.{(i % 2 == 0 ? "flac" : "mp3")}");
            var metadata = new TrackMetadata(i == 0 ? "星際旅行" : i == 1 ? "星际 Live" : $"Track {i:000}",
                i < 2 ? "周杰倫" : "Aurora", i < 2 ? "旅程" : "North", "", i % 7 * 30 + 60,
                i % 2 == 0 ? "FLAC" : "MP3", i % 2 == 0 ? 96000 : 44100, i % 2 == 0 ? 24 : 16,
                i + 1, 1, i < 2 ? 2026 : 2020, i < 2 ? "Pop" : "Rock");
            TrackSource? source = i < 428 ? null : new(i == 428 ? "cue" : "osu", file, file + ".definition", "fixture", 100, 1000 + i, 10, 1);
            entries.Add(new(new(file, LibraryPath.Key(file), 100, 1000 + i, Source: source), ScanEntryKind.Metadata, metadata));
        }
        long sequence = 0;
        foreach (var batch in entries.Chunk(128)) await db.CommitScanBatchAsync(job, ++sequence, batch, token);
        await db.FinishScanAsync(job, ScanStatus.Completed, entries.Count, false, null, token);
        void Check(bool condition, string description)
        { if (!condition) throw new InvalidOperationException(description); }
        Check((await db.GetTracksAsync(new(Search: "星际旅行"), token)).Items.Count == 1, "cross-script CJK search");
        Check((await db.GetTracksAsync(new(Search: "旅行"), token)).Items.Count == 1, "short CJK substring");
        Check((await db.GetTracksAsync(new(Search: "/artist 周杰伦 /year 2026 /-Live"), token)).Items.Count == 1, "field/year/exclude combination");
        Check((await db.GetTracksAsync(new(Search: "/genre rock /exact \"Track 100\""), token)).Items.Count == 1, "genre/exact phrase");
        Check((await db.GetTracksAsync(new(Search: "/path track000"), token)).Items.Count == 1, "path command");
        Check((await db.GetTracksAsync(new(Search: "Ｔｒａｃｋ １００"), token)).Items.Count == 1, "NFKC search");
        Check((await db.GetAlbumsAsync(new(Search: "旅"), token)).Total == 1, "album CJK substring");
        Check((await db.GetArtistsAsync(new(Search: "杰伦"), token)).Total == 1, "artist cross script");
        Check((await db.GetTracksAsync(new(Search: "/title:\"Track 100\""), token)).Items.Count == 1, "inline quoted field");
        Check((await db.GetAlbumsAsync(new(Search: "/decade 2020s /genre Pop /format 24bit"), token)).Total == 1, "album focus commands");
        Check((await db.GetAlbumsAsync(new(Search: "/format FLAC", Filter: new(Rate: AudioRate.Hz44100)), token)).Total == 0, "panel and command intersection");
        var filter = new LibraryFilter(FileFormat.Flac, AudioRate.Hz96000, 24, 2026, "Pop", Path.Combine(root, "A"));
        var filtered = await db.GetTracksAsync(new(Filter: filter), token);
        Check(filtered.Items.Count == 1, "combined format/rate/depth/year/genre/folder");
        Check((await db.GetAlbumsAsync(new(Filter: filter), token)).Total == 1, "album filters");
        Check((await db.GetArtistsAsync(new(Filter: filter), token)).Total == 1, "artist filters");
        Check((await db.GetAlbumsAsync(new(Filter: filter with { Rate = AudioRate.Hz44100 }), token)).Total == 0, "same-member album intersection");
        Check((await db.GetTracksAsync(new(Filter: new(Source: LibrarySource.Cue)), token)).Items.Count == 1, "CUE source");
        Check((await db.GetTracksAsync(new(Filter: new(Source: LibrarySource.Osu)), token)).Items.Count == 1, "osu source");
        Check((await db.GetTracksAsync(new(200, Filter: new(Source: LibrarySource.Files)), token)).Items.All(row => row.Source is null), "plain file source");
        var durationOrder = await db.GetTracksAsync(new(200, Order: [new(TrackSort.Duration, true), new(TrackSort.Title)]), token);
        Check(durationOrder.Items.Zip(durationOrder.Items.Skip(1)).All(pair => pair.First.DurationSeconds >= pair.Second.DurationSeconds), "descending duration order");
        foreach (var sort in Enum.GetValues<TrackSort>())
            foreach (var descending in new[] { false, true })
            {
                var query = new TrackQuery(Limit: 43, Order: [new(sort, descending), new(sort == TrackSort.Title ? TrackSort.Duration : TrackSort.Title)]);
                var ids = new HashSet<string>(); TrackCursor? cursor = null;
                do
                {
                    var page = await db.GetTracksAsync(query with { After = cursor }, token);
                    Check(page.Items.All(row => ids.Add(row.Id)), "overlapping sort pages: " + sort);
                    cursor = page.Next;
                } while (cursor is not null);
                Check(ids.Count == 430, "lost sort rows: " + sort);
            }
        var first = await db.GetTracksAsync(new(Limit: 1), token);
        try { await db.GetTracksAsync(new(Limit: 1, Search: "other", After: first.Next), token); throw new Exception("cursor query change accepted"); }
        catch (ArgumentException) { }
        var favorite = filtered.Items[0];
        await db.SetTrackFavoriteAsync(favorite.Id, true, token);
        Check((await db.GetTracksAsync(new(Filter: new(FavoriteOnly: true)), token)).Items.Single().Id == favorite.Id, "favorite write/filter");
        try { await db.GetTracksAsync(new(After: first.Next), token); throw new Exception("stale cursor accepted"); }
        catch (LibraryChangedException) { }
        var albums = await db.GetAlbumsAsync(new(Filter: new(FavoriteOnly: true)), token);
        Check(albums.Total == 0, "album favorites use album owner");
        var album = (await db.GetAlbumsAsync(new(Search: "旅"), token)).Items.Single();
        await db.SetAlbumStateAsync(album.Id, true, 3, token);
        Check((await db.GetAlbumsAsync(new(Filter: new(FavoriteOnly: true)), token)).Total == 1, "favorite cached count invalidation");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await db.GetTracksAsync(new(Search: "Track"), cancelled.Token); throw new Exception("cancel ignored"); }
        catch (OperationCanceledException) { }
        var legacyPath = Path.Combine(root, "generated-v5.sqlite");
        MigrationFixture.ExtractV5(legacyPath, root);
        await using (var migrated = await LibraryDatabase.OpenAsync(legacyPath, token))
            Check((await migrated.GetTracksAsync(new(Search: "/artist Singer"), token)).Items.Count == 2, "v5 search index backfill");
        Console.WriteLine("Search migration: 430 rows; commands, CJK/NFKC, shared filter intersections, 24 sort directions with full pagination, cursor identity, favorite revision and cancellation passed.");
    }
}
