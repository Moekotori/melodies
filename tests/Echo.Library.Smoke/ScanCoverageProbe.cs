using System.Text;
using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

internal static class ScanCoverageProbe
{
    public static async Task RunAsync(string root, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        await SourceMetadataProbe.RunAsync(token);
        foreach (var embedded in new[] { false, true })
        {
            var directory = Path.Combine(root, embedded ? "ignored-flac" : "ignored-cue"); Directory.CreateDirectory(directory);
            var definition = Path.Combine(directory, embedded ? "album.flac" : "album.cue");
            if (embedded) CueFixtures.Flac(definition);
            else
            {
                CueFixtures.Wave(Path.Combine(directory, "audio.wav"));
                await File.WriteAllTextAsync(definition, "FILE 'audio.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0\nTRACK 02 AUDIO\nINDEX 01 75", token);
            }
            await using var db = await LibraryDatabase.OpenAsync(Path.Combine(root, embedded ? "flac.sqlite" : "cue.sqlite"), token);
            ScanCoordinator Scanner(Func<string, bool>? ignored = null) => new(db, new LegacyFolderDiscovery(db, ignored: ignored), () => new LocalMetadataReader());
            var original = await Scanner().ScanAsync(new(directory), cancellation: token);
            Check(original.Inserted == 2 && original.Failed == 0, "ignored-source seed import");
            var old = await db.GetTracksAsync(new(20), token);
            // An ignored definition must not be opened, even after its contents become unreadable.
            await File.WriteAllTextAsync(definition, "ignored contents", token);
            CueFixtures.Wave(Path.Combine(directory, "other.wav"));
            var report = await Scanner(path => path == definition).ScanAsync(new(directory, DetectMissing: true), cancellation: token);
            var page = await db.GetTracksAsync(new(20), token);
            Check(report.Status == ScanStatus.Completed && report.Failed == 0 && report.MarkedMissing == 0 && report.Inserted == 1
                && page.Items.Count == 3 && old.Items.All(x => page.Items.Any(y => x.Id == y.Id)),
                $"ignored definition lost indexed segments: embedded={embedded}, {report.Status}, failed={report.Failed}, inserted={report.Inserted}, missing={report.MarkedMissing}, visible={page.Items.Count}");
        }

        var cross = Path.Combine(root, "broken-cross-directory");
        Directory.CreateDirectory(Path.Combine(cross, "sheets")); Directory.CreateDirectory(Path.Combine(cross, "audio"));
        CueFixtures.Wave(Path.Combine(cross, "audio", "disc.wav"));
        var crossCue = Path.Combine(cross, "sheets", "album.cue");
        await File.WriteAllTextAsync(crossCue, "FILE '../audio/disc.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0\nTRACK 02 AUDIO\nINDEX 01 75", token);
        await using (var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "broken-cross.sqlite"), token))
        {
            ScanCoordinator Scanner() => new(db, new LegacyFolderDiscovery(db), () => new LocalMetadataReader());
            Check((await Scanner().ScanAsync(new(cross), cancellation: token)).Inserted == 2, "cross-directory protection seed");
            await File.WriteAllTextAsync(crossCue, "", token);
            CueFixtures.Wave(Path.Combine(cross, "audio", "other.wav"));
            var broken = await Scanner().ScanAsync(new(cross, DetectMissing: true), cancellation: token);
            var visible = await db.GetTracksAsync(new(20), token);
            Check(broken.Status == ScanStatus.Completed && broken.Failed == 1 && broken.Inserted == 1 && broken.MarkedMissing == 0
                && visible.Items.Count == 3 && visible.Items.Count(x => x.Source?.Kind == "cue") == 2,
                "unreadable CUE lost cross-directory sources from its existing index");
        }

        var longLabels = Path.Combine(root, "long-labels"); Directory.CreateDirectory(longLabels);
        var audio = Path.Combine(longLabels, "disc.wav"); CueFixtures.Wave(audio);
        var label = new string('曲', 512);
        var cueText = new StringBuilder($"TITLE '{label}'\nPERFORMER '{label}'\nFILE 'disc.wav' WAVE\n");
        for (var i = 0; i < 128; i++) cueText.Append($"TRACK {i + 1} AUDIO\nTITLE '{label}'\nPERFORMER '{label}'\nINDEX 01 {i}\n");
        await File.WriteAllTextAsync(Path.Combine(longLabels, "disc.cue"), cueText.ToString(), token);
        await using (var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "long-labels.sqlite"), token))
        {
            var report = await new ScanCoordinator(db, new LegacyFolderDiscovery(db), () => new LocalMetadataReader())
                .ScanAsync(new(longLabels, Workers: 4, BatchSize: 256), cancellation: token);
            Check(report.Status == ScanStatus.Completed && report.Inserted == 128 && report.Failed == 0, "valid escaped source text overflowed a scan batch");
            var repeat = await new ScanCoordinator(db, new LegacyFolderDiscovery(db), () => new LocalMetadataReader())
                .ScanAsync(new(longLabels, Workers: 4, BatchSize: 256), cancellation: token);
            Check(repeat.Unchanged == 128 && repeat.Failed == 0 && repeat.Status == ScanStatus.Completed, "unchanged source payload budget");
            var protectedScan = await new ScanCoordinator(db, new LegacyFolderDiscovery(db, ignored: path => path.EndsWith(".cue", StringComparison.Ordinal)), () => new LocalMetadataReader())
                .ScanAsync(new(longLabels, DetectMissing: true), cancellation: token);
            Check(protectedScan.Unchanged == 128 && protectedScan.Failed == 0 && protectedScan.MarkedMissing == 0,
                "ignored definition membership must page beyond 64 tracks");
        }
        await using (var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "source-limit.sqlite"), token))
        {
            var snapshot = AudioFixture.Snapshot(audio);
            var source = new TrackSource("cue", audio, audio, "s", snapshot.SizeBytes, snapshot.ModifiedTicks,
                snapshot.SizeBytes, snapshot.ModifiedTicks, CoverPath: new string('图', 4000), EndSeconds: 1);
            var oversized = snapshot with { Path = audio + "#cueTrack=1", PathKey = LibraryPath.Key(audio + "#cueTrack=1"), Source = source };
            var report = await new ScanCoordinator(db, new FixedDiscovery([oversized, snapshot]), () => new LocalMetadataReader())
                .ScanAsync(new(longLabels, DetectMissing: true), cancellation: token);
            var issues = await db.GetScanIssuesAsync(report.JobId, cancellation: token);
            Check(report.Status == ScanStatus.Completed && report.Failed == 1 && report.Inserted == 1 && report.MarkedMissing == 0
                && issues.Single().Code == "source_payload_limit", "oversized source failed the whole writer instead of one file");
        }
        Console.WriteLine("PASS scan coverage: ignored sidecar/embedded CUE preservation, sibling continuation, escaped source budgets and oversized-file isolation");
    }

    private sealed class FixedDiscovery(FileSnapshot[] files) : IFileDiscovery
    {
        public IEnumerable<FileSnapshot> Enumerate(string root, bool full, CancellationToken token, bool force = false)
        { foreach (var file in files) { token.ThrowIfCancellationRequested(); yield return file; } }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
