using Echo.Contracts.Library;
using Echo.Infrastructure.Library;

internal static class StressPageProbe
{
    public static async Task RunAsync(string root, string label, CancellationToken token)
    {
        StressLibraryFixture.ValidateRoot(root, false);
        await StressLibraryFixture.HistoryAsync(root, token);
        await using var metrics = new StressReadMetrics(root, label);
        await using var db = await LibraryDatabase.OpenAsync(StressLibraryFixture.Database(root), token);
        var media = StressLibraryFixture.Media(root);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var stats = await db.GetStatsAsync(token);
        Check(stats is { Tracks: StressLibraryFixture.Tracks, Missing: 0 }, "400k committed rows required");
        await metrics.MeasureAsync("home", async t =>
        {
            var home = await db.GetHomeAsync(today, t);
            Check(home.Library is { Tracks: StressLibraryFixture.Tracks, Artists: StressLibraryFixture.Artists,
                Albums: StressLibraryFixture.Artists, Folders: StressLibraryFixture.Artists }, "home actual scale");
        }, token, targetMs: 150);
        await metrics.MeasureAsync("songs-first", async t =>
            Check((await db.GetTracksAsync(new(200), t)).Items.Count == 200, "song first"), token);
        await metrics.MeasureAsync("songs-deep-399800", async t =>
        {
            var page = await db.GetTracksAsync(new(200, Offset: 399800), t);
            Check(page.Items.Count == 200 && page.Items[0].Title == "Track 399800" && page.Next is null, "song deep");
        }, token);
        await metrics.MeasureAsync("songs-search-exact", async t =>
            Check((await db.GetTracksAsync(new(200, Search: "Track 399900"), t)).Items.Count == 1, "song search"), token, targetMs: 150);
        await metrics.MeasureAsync("albums-first", async t =>
        { var page = await db.GetAlbumsAsync(new(), t); Check(page.Total == 100000 && page.Items.Count == 200, "album first"); }, token);
        await metrics.MeasureAsync("albums-deep-99800", async t =>
        { var page = await db.GetAlbumsAsync(new(99800), t); Check(page.Total == 100000 && page.Items.Count == 200, "album deep"); }, token);
        await metrics.MeasureAsync("artists-first", async t =>
        { var page = await db.GetArtistsAsync(new(), t); Check(page.Total == 100000 && page.Items.Count == 200, "artist first"); }, token);
        await metrics.MeasureAsync("artists-deep-99800", async t =>
        { var page = await db.GetArtistsAsync(new(99800), t); Check(page.Total == 100000 && page.Items.Count == 200, "artist deep"); }, token);
        await metrics.MeasureAsync("artists-search-exact", async t =>
            Check((await db.GetArtistsAsync(new(Search: "Artist 099999"), t)).Total == 1, "artist search"), token, targetMs: 150);
        await metrics.MeasureAsync("folders-root", async t =>
            Check((await db.GetFoldersAsync(new(), t)).Items.Count == 1, "folder root"), token);
        await metrics.MeasureAsync("folders-100-shards", async t =>
            Check((await db.GetFoldersAsync(new(ParentPath: media), t)).Items.Count == 100, "folder shards"), token);
        await metrics.MeasureAsync("folders-deep-shard", async t =>
            Check((await db.GetFoldersAsync(new(ParentPath: Path.Combine(media, "shard-099"), Offset: 800), t)).Items.Count == 200, "folder children"), token);
        await metrics.MeasureAsync("folder-tracks-first", async t =>
        { var page = await db.GetFolderTracksAsync(new(media), t); Check(page.Total == 400000 && page.Items.Count == 200, "folder first"); }, token);
        await metrics.MeasureAsync("folder-tracks-deep-399800", async t =>
        { var page = await db.GetFolderTracksAsync(new(media, Offset: 399800), t); Check(page.Total == 400000 && page.Items.Count == 200, "folder deep"); }, token);
        await metrics.MeasureAsync("genres", async t =>
            Check((await db.GetGenresAsync(new(), t)).Items.Count == 100, "genres"), token);
        await metrics.MeasureAsync("cancelled-search", async _ =>
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await db.GetTracksAsync(new(200, Search: "Track"), cancelled.Token); }
            catch (OperationCanceledException) { return; }
            throw new InvalidOperationException("Cancellation ignored.");
        }, token, 3);
        Console.WriteLine($"STRESS_READ_COMPLETE tracks={stats.Tracks} artists=100000 folders=100000 failedScenarios={metrics.Failures}");
        StressQueryPlans.Capture(root, label);
        if (metrics.Failures != 0) throw new InvalidOperationException("Stress scenarios failed; see CSV.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
