using System.Text;
using System.Security.Cryptography;
using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Media;
using Echo.Contracts.Scanning;
using Echo.Core.Media;
using Echo.Core.Protocol;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;
using Echo.Infrastructure.Playback;

internal static class CueCompatibilityProbe
{
    public static async Task RunAsync(string root, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var checks = 0;
        void Check(bool value, string label)
        { if (!value) throw new InvalidOperationException(label); checks++; }
        Check(AudioFormatCatalog.DiscoveryExtensions.All(x => ScanFormats.Supports(x.ToUpperInvariant()))
            && !ScanFormats.Supports(".jpg") && !ScanFormats.Supports(".flac.bak"), "span-based extension catalogue parity");
        // Warm the span lookup before measuring only the discovery predicate on this thread.
        for (var i = 0; i < 1000; i++)
        { _ = ScanFormats.Supports(".FLAC"); _ = ScanFormats.Supports(".unknown"); }
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100_000; i++)
        { _ = ScanFormats.Supports(".FLAC"); _ = ScanFormats.Supports(".unknown"); }
        var predicateAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        // Allow fixed runtime/tiered-JIT bookkeeping, while rejecting even one object per file.
        Check(predicateAllocated < 64 * 1024, "extension discovery allocated per file: " + predicateAllocated);
        Console.WriteLine($"DISCOVERY_PREDICATE calls=200000 allocatedBytes={predicateAllocated}");
        var syntax = Path.Combine(root, "syntax"); Directory.CreateDirectory(syntax);
        CueFixtures.Wave(Path.Combine(syntax, "disc one.wav"));
        CueFixtures.Wave(Path.Combine(syntax, "disc two.wav"));
        var sheet = Path.Combine(syntax, "album.cue");
        ScanCueResult Read() => ScanCueReader.Read(new() { FilePath = sheet, RootPath = syntax, IsolateFileErrors = true }, token);
        async Task<ScanCueResult> Parse(string text)
        { await File.WriteAllTextAsync(sheet, text, token); return Read(); }
        var first = await Parse("TITLE 'Album'\nPERFORMER 'Artist'\nFILE disc one.wav WAVE\n"
            + "TRACK 00 AUDIO\nTITLE 'Artist\\'s intro'\nINDEX 01 0\nTRACK 01 AUDIO\nINDEX 01 150");
        Check(first.Issue is null && first.Tracks.Count == 2 && first.Tracks[0].TrackNumber == 0
            && first.Tracks[0].CueTrack is { Title: "Artist's intro", EndSeconds: 2 }, "extended CUE syntax / frame offsets");
        var multi = await Parse("FILE 'disc one.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0\nTRACK 02 AUDIO\n"
            + "TITLE 'Second file'\nFILE 'disc two.wav' WAVE\nINDEX 01 0\nTRACK 03 AUDIO\nINDEX 01 75");
        Check(multi.Issue is null && multi.Tracks.Count == 3 && multi.Tracks[0].CueTrack!.EndSeconds is null
            && multi.Tracks[1].CueTrack is { Title: "Second file", StartSeconds: 0, EndSeconds: 1 }, "FILE inside TRACK / independent timelines");
        var mixed = await Parse("FILE 'disc one.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0\nTRACK 02 MODE1/2352\n"
            + "INDEX 00 75\nINDEX 01 75\nFILE 'disc two.wav' WAVE\nTRACK 03 AUDIO\nINDEX 01 0\n"
            + "FILE 'disc one.wav' WAVE\nTRACK 04 AUDIO\nINDEX 01 150");
        Check(mixed.Issue is null && mixed.Tracks.Select(x => x.TrackNumber).SequenceEqual([1, 3, 4])
            && mixed.Tracks[0].CueTrack!.EndSeconds == 1, "mixed data tracks and nonconsecutive source boundaries");
        foreach (var text in new[]
        {
            "FILE 'disc one.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0\nTRACK 01 AUDIO\nINDEX 01 75",
            "FILE 'disc one.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 150\nTRACK 02 AUDIO\nINDEX 01 75",
            "FILE 'disc one.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:75",
            "FILE 'disc one.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0\nTRACK 02 MODE1/2352\nINDEX 00 bad"
        })
        {
            var invalid = await Parse(text);
            Check(invalid.Issue == "source_unreadable" && invalid.Tracks.Count == 0
                && invalid.SourcePaths!.Contains(Path.Combine(syntax, "disc one.wav")), "invalid ranges must protect named sources");
        }
        CueFixtures.Wave(Path.Combine(root, "outside.wav"));
        Check((await Parse("FILE '../outside.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0")).Tracks.Count == 0, "CUE root escape");
        await File.WriteAllBytesAsync(sheet, new byte[2 * 1024 * 1024 + 1], token);
        Check(Read().Issue == "source_limit", "CUE byte budget");
        await File.WriteAllTextAsync(sheet, "FILE 'disc one.wav' WAVE\nTRACK 01 AUDIO\nTITLE 'Single'\nINDEX 01 75", token);
        var single = new SelectedFileDiscovery(sheet).Enumerate(syntax, false, token).ToArray();
        Check(single.Length == 1 && single[0].Source is { Kind: "cue", Title: "Single", StartSeconds: 1 }, "single-track CUE selected import");
        var playback = await PlaybackFileResolver.ResolveAsync([sheet], token);
        Check(playback.Count == 1 && playback[0].Source?.Range?.StartSeconds == 1, "selected CUE playback descriptor");
        CueFixtures.Flac(Path.Combine(syntax, "rip.flac"));
        var lossless = await Parse("FILE 'rip.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0");
        Check(lossless.Issue is null && lossless.Tracks.Single().AudioPath.EndsWith("rip.flac", StringComparison.Ordinal), "unique lossless rip source");
        await File.WriteAllTextAsync(Path.Combine(syntax, "rip.ape"), "ambiguous candidate", token);
        Check((await Parse("FILE 'rip.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0")).Issue == "source_unreadable", "ambiguous rip source cannot guess an album");

        var encodings = Path.Combine(root, "encodings"); Directory.CreateDirectory(encodings);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (var (encoding, name) in new[] { ("shift_jis", "日本語"), ("GB18030", "简体中文"), ("windows-1252", "café") })
        {
            var audio = Path.Combine(encodings, name + ".wav"); CueFixtures.Wave(audio);
            var path = Path.Combine(encodings, encoding + ".cue");
            await File.WriteAllBytesAsync(path, Encoding.GetEncoding(encoding).GetBytes($"FILE '{name}.wav' WAVE\nTRACK 01 AUDIO\nTITLE '{name}'\nINDEX 01 0"), token);
            var result = ScanCueReader.Read(new() { FilePath = path, RootPath = encodings, IsolateFileErrors = true }, token);
            Check(result.Issue is null && result.Tracks.Single().CueTrack!.Title == name, "CUE encoding " + encoding);
        }
        foreach (var encoding in new Encoding[] { new UTF32Encoding(false, true), new UTF32Encoding(true, false), new UnicodeEncoding(true, false) })
        {
            var path = Path.Combine(encodings, "unicode.cue");
            await File.WriteAllTextAsync(path, "FILE '日本語.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0", encoding, token);
            var result = ScanCueReader.Read(new() { FilePath = path, RootPath = encodings, IsolateFileErrors = true }, token);
            Check(result.Issue is null && result.Tracks.Count == 1, "Unicode CUE " + encoding.WebName);
        }

        var binary = Path.Combine(root, "binary"); Directory.CreateDirectory(binary);
        var flac = Path.Combine(binary, "album.flac");
        foreach (var cd in new[] { false, true })
        {
            CueFixtures.Flac(flac, cd: cd);
            var result = ScanCueReader.Read(new() { FilePath = flac, Embedded = true, RootPath = binary, IsolateFileErrors = true }, token);
            Check(result.Issue is null && result.Tracks.Count == 2 && result.Tracks[0].CueTrack!.EndSeconds == 2
                && result.Tracks[1].CueTrack is { PregapStartSeconds: 1, StartSeconds: 2, EndSeconds: 4 }, "binary CUESHEET sample offsets / leadout");
        }
        await using (var reader = new LocalMetadataReader())
        {
            var expanded = new LegacyFolderDiscovery().Enumerate(binary, false, token).ToArray();
            Check(expanded.Length == 2 && expanded.All(x => x.Source?.Kind == "cue"), "binary discovery suppresses whole-file duplicate");
            Check((await reader.ReadAsync(expanded[1], token)).DurationSeconds == 2, "binary segment metadata");
            foreach (var extension in new[] { ".wave", ".bwf" })
            {
                var alias = Path.Combine(root, "alias" + extension); CueFixtures.Wave(alias);
                Check((await reader.ReadAsync(AudioFixture.Snapshot(alias), token)).DurationSeconds == 4, "managed WAV alias " + extension);
            }
        }
        CueFixtures.Flac(flac, "TRACK 03 AUDIO\nTITLE 'Text wins'\nINDEX 01 75");
        var textual = new SelectedFileDiscovery(flac).Enumerate(binary, false, token).Single();
        Check(textual.Source is { Title: "Text wins", TrackNumber: 3 }, "textual CUE authority / one embedded track");
        CueFixtures.Flac(flac, "TRACK 03 AUDIO\nINDEX 01 bad");
        Check(new LegacyFolderDiscovery().Enumerate(binary, false, token).Single().Issue == "source_unreadable", "invalid explicit text cannot silently fall through to binary");
        CueFixtures.Flac(flac, invalid: true);
        Check(ScanCueReader.Read(new() { FilePath = flac, Embedded = true, IsolateFileErrors = true }, token).Issue == "source_unreadable", "invalid binary isolation");
        File.Delete(flac);
        Check(!File.Exists(flac), "CUE file handle released");

        var databaseRoot = Path.Combine(root, "database"); Directory.CreateDirectory(databaseRoot);
        var audioPath = Path.Combine(databaseRoot, "disc.wav"); CueFixtures.Wave(audioPath);
        var cuePath = Path.Combine(databaseRoot, "disc.cue");
        await File.WriteAllTextAsync(cuePath, "FILE 'disc.wav' WAVE\nTRACK 01 AUDIO\nTITLE 'One'\nINDEX 01 0\nTRACK 02 AUDIO\nTITLE 'Two'\nINDEX 01 150", token);
        await using var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "library.sqlite"), token);
        ScanCoordinator Scanner() => new(db, new LegacyFolderDiscovery(db), () => new LocalMetadataReader());
        var imported = await Scanner().ScanAsync(new(databaseRoot, DetectMissing: true), cancellation: token);
        Check(imported.Inserted == 2 && imported.Failed == 0, "CUE database import");
        var repeated = await Scanner().ScanAsync(new(databaseRoot, DetectMissing: true), cancellation: token);
        Check(repeated.Unchanged == 2 && repeated.Inserted + repeated.Updated + repeated.Failed == 0, "CUE incremental metadata reuse");
        await File.AppendAllTextAsync(cuePath, "\nFILE 'missing.wav' WAVE\nTRACK 03 AUDIO\nINDEX 01 0", token);
        CueFixtures.Wave(Path.Combine(databaseRoot, "other.wav"));
        var isolated = await Scanner().ScanAsync(new(databaseRoot, DetectMissing: true), cancellation: token);
        var visible = await db.GetTracksAsync(new(20), token);
        Check(isolated.Status == ScanStatus.Completed && isolated.Failed == 1 && isolated.Inserted == 1 && isolated.MarkedMissing == 0,
            "broken CUE must continue sibling import without missing cleanup");
        Check(visible.Items.Count == 3 && visible.Items.Count(x => x.Source?.Kind == "cue") == 2
            && visible.Items.All(x => x.Path != audioPath), "failed sheet must preserve existing virtual tracks without whole-file replacement");

        var upgrade = Path.Combine(root, "upgrade"); Directory.CreateDirectory(upgrade);
        var oldFlac = Path.Combine(upgrade, "old.flac"); CueFixtures.Flac(oldFlac);
        var oldFile = AudioFixture.Snapshot(oldFlac);
        // Fingerprint produced before binary CUESHEET discovery existed; physical media is unchanged.
        var oldFacts = FormattableString.Invariant($"{oldFile.PathKey}\n{oldFile.SizeBytes}\n{oldFile.ModifiedTicks}\n{oldFile.PathKey}\n{oldFile.SizeBytes}\n{oldFile.ModifiedTicks}\n");
        var oldStamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldFacts)));
        var oldSource = new TrackSource("local", oldFlac, oldFlac, oldStamp, oldFile.SizeBytes, oldFile.ModifiedTicks, oldFile.SizeBytes, oldFile.ModifiedTicks);
        await using (var reader = new LocalMetadataReader())
        {
            var job = await db.BeginScanAsync(upgrade, false, token);
            await db.CommitScanBatchAsync(job, 1, [new(oldFile with { Source = oldSource }, ScanEntryKind.Metadata,
                await reader.ReadAsync(oldFile, token))], token);
            await db.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, token);
        }
        var upgraded = await Scanner().ScanAsync(new(upgrade), cancellation: token);
        var upgradedTracks = await db.GetTracksAsync(new(20), token);
        Check(upgraded.Inserted == 2 && upgraded.Failed == 0 && upgradedTracks.Items.Count(x => x.Source?.AudioPath == oldFlac) == 2
            && upgradedTracks.Items.All(x => x.Path != oldFlac), "existing FLAC must discover binary CUE after upgrade without force");

        var crossRoot = Path.Combine(root, "cross-directory"); Directory.CreateDirectory(crossRoot);
        foreach (var name in new[] { "audio-first", "cues", "audio-last" }) Directory.CreateDirectory(Path.Combine(crossRoot, name));
        CueFixtures.Wave(Path.Combine(crossRoot, "audio-first", "one.wav"));
        CueFixtures.Wave(Path.Combine(crossRoot, "audio-last", "two.wav"));
        await File.WriteAllTextAsync(Path.Combine(crossRoot, "cues", "both.cue"),
            "TITLE 'Cross folder'\nFILE '../audio-first/one.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0\n"
            + "FILE '../audio-last/two.wav' WAVE\nTRACK 02 AUDIO\nINDEX 01 0", token);
        var cross = await Scanner().ScanAsync(new(crossRoot, DetectMissing: true), cancellation: token);
        var crossPage = await db.GetTracksAsync(new(100), token);
        var crossTracks = crossPage.Items.Where(x => x.Source?.DefinitionPath == Path.Combine(crossRoot, "cues", "both.cue")).ToArray();
        Check(cross.Inserted == 2 && cross.Failed == 0 && crossTracks.Length == 2
            && crossTracks.All(x => x.Source?.Kind == "cue"), $"cross-directory sources must not replace virtual tracks: {cross.Status}, inserted={cross.Inserted}, failed={cross.Failed}, visible={crossTracks.Length}");

        var spillRoot = Path.Combine(root, "spill"); Directory.CreateDirectory(spillRoot);
        for (var i = 0; i < 1050; i++)
        {
            token.ThrowIfCancellationRequested();
            AudioFixture.Wave(Path.Combine(spillRoot, i + ".wav"), "Spill");
            await File.WriteAllTextAsync(Path.Combine(spillRoot, i + ".cue"), $"FILE '{i}.wav' WAVE\nTRACK 01 AUDIO\nINDEX 01 0", token);
        }
        var priorScratch = Directory.GetFiles(Path.GetTempPath(), "echo-folder-*.sqlite").ToHashSet(StringComparer.Ordinal);
        var discovered = 0;
        foreach (var file in new LegacyFolderDiscovery().Enumerate(spillRoot, false, token))
        {
            if (file.Source?.Kind != "cue" || file.Issue is not null) throw new Exception("Spilled source set leaked whole audio");
            discovered++;
        }
        Check(discovered == 1050 && !Directory.GetFiles(Path.GetTempPath(), "echo-folder-*.sqlite").Any(x => !priorScratch.Contains(x)),
            "scan-owned spilled source set / transaction release");
        using (var abandoned = new LegacyFolderDiscovery().Enumerate(spillRoot, false, token).GetEnumerator())
            for (var i = 0; i < 1040; i++) if (!abandoned.MoveNext()) throw new Exception("Short spill fixture");
        Check(!Directory.GetFiles(Path.GetTempPath(), "echo-folder-*.sqlite").Any(x => !priorScratch.Contains(x)), "early enumeration disposal leaked scratch transaction");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { _ = ScanCueReader.Read(new() { FilePath = cuePath }, cancelled.Token); throw new Exception("CUE ignored cancellation"); }
        catch (OperationCanceledException) { checks++; }
        Console.WriteLine($"PASS {checks} CUE/format scan checks: syntax, encodings, binary/text precedence, ranges, single tracks, incremental reuse and failure isolation");
    }
}
