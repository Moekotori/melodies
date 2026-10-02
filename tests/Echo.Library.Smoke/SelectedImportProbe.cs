using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

internal static class SelectedImportProbe
{
    public static async Task RunAsync(string root, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var selected = Path.Combine(root, "选中.wav");
        var sibling = Path.Combine(root, "其他.wav");
        AudioFixture.Wave(selected, "Selected"); AudioFixture.Wave(sibling, "Sibling");
        await using var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "library-" + Guid.NewGuid().ToString("N") + ".sqlite"), token);
        var scan = new ScanCoordinator(db, new SelectedFileDiscovery(selected, db), () => new LocalMetadataReader());
        var first = await scan.ScanAsync(new(root), cancellation: token);
        var page = await db.GetTracksAsync(new(), token);
        Check(first.Status == ScanStatus.Completed && first.Inserted == 1 && first.Discovered == 1 &&
            page.Items.Count == 1 && page.Items[0].Path == selected, "Single-file import included a sibling or failed.");
        var repeat = await scan.ScanAsync(new(root), cancellation: token);
        Check(repeat.Unchanged == 1 && repeat.Inserted == 0, "Repeated drop was not incremental.");
        var folder = await new ScanCoordinator(db, new LegacyFolderDiscovery(db), () => new LocalMetadataReader())
            .ScanAsync(new(root), cancellation: token);
        Check(folder.Status == ScanStatus.Completed && folder.Unchanged == 1 && folder.Inserted == 1 &&
            (await db.GetStatsAsync(token)).Tracks == 2, "Folder import regressed or rewrote the selected song.");
        // Partial imports cannot mark unselected tracks missing, even if called incorrectly.
        var partial = await scan.ScanAsync(new(root, DetectMissing: true), cancellation: token);
        Check(partial.Status == ScanStatus.Failed && partial.MarkedMissing == 0 && (await db.GetStatsAsync(token)).Missing == 0,
            "Partial selection allowed missing cleanup.");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { _ = new SelectedFileDiscovery(selected).Enumerate(root, false, cancel.Token).First(); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { }
        var definition = Path.Combine(root, "album.cue");
        await File.WriteAllTextAsync(definition, """
            FILE "选中.wav" WAVE
              TRACK 01 AUDIO
                TITLE "First"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                TITLE "Second"
                INDEX 01 00:00:01
            """, token);
        var cue = new SelectedFileDiscovery(definition).Enumerate(root, false, token).ToArray();
        Check(cue.Length == 2 && cue.All(file => file.Source is { Kind: "cue" } && file.Source.AudioPath == selected),
            "Selected CUE expansion lost its source boundary.");
        var osu = Path.Combine(root, "map.osu");
        await File.WriteAllTextAsync(osu, """
            osu file format v14
            [General]
            AudioFilename: 选中.wav
            Mode: 0
            [Metadata]
            Title:Selected
            Artist:Artist
            Creator:Creator
            Version:Normal
            BeatmapSetID:1
            [TimingPoints]
            0,500,4,2,1,100,1,0
            """, token);
        var maps = new SelectedFileDiscovery(osu).Enumerate(root, false, token).ToArray();
        Check(maps.Length == 1 && maps[0].Source is { Kind: "osu" } && maps[0].Source!.AudioPath == selected,
            "Selected osu definition imported unrelated audio.");
        Console.WriteLine("Selected import: file isolation, duplicate drop, folder, missing safety, cancellation, CUE and osu passed.");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
