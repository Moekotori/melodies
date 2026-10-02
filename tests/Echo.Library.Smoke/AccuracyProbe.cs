using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

internal static class AccuracyProbe
{
    public static async Task<int> RunAsync(string root, CancellationToken cancellation)
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "changing.wav");
        AudioFixture.Wave(file, "Original");
        await using var database = await LibraryDatabase.OpenAsync(Path.Combine(root, "accuracy.sqlite"), cancellation);
        var scanner = new ScanCoordinator(database, new DirectoryDiscovery(), () => new ChangingReader());
        var report = await scanner.ScanAsync(new(root), cancellation: cancellation);
        if (report.Failed != 1 || report.Inserted != 0 || (await database.GetStatsAsync(cancellation)).Tracks != 0)
            throw new InvalidOperationException("A changing file was persisted with a stale fingerprint.");
        var issues = await database.GetScanIssuesAsync(report.JobId, cancellation: cancellation);
        if (issues.Count != 1 || issues[0].Code != "file_changed_during_scan")
            throw new InvalidOperationException("The failed file is not diagnosable.");
        var prior = AudioFixture.Snapshot(file);
        AudioFixture.Wave(file, "Different");
        File.SetLastWriteTimeUtc(file, new DateTime(prior.ModifiedTicks, DateTimeKind.Utc));
        var job = await database.BeginScanAsync(root, false, cancellation);
        await using var reader = new LocalMetadataReader();
        var metadata = await reader.ReadAsync(AudioFixture.Snapshot(file), cancellation);
        var valid = new ScanEntry(AudioFixture.Snapshot(file), ScanEntryKind.Metadata, metadata);
        try
        {
            var invalidPath = Path.Combine(root, "invalid.wav");
            var invalid = valid with
            {
                File = valid.File with { Path = invalidPath, PathKey = LibraryPath.Key(invalidPath) },
                Metadata = metadata with { DurationSeconds = double.NaN }
            };
            await database.CommitScanBatchAsync(job, 1, [valid, invalid], cancellation);
            throw new InvalidOperationException("Invalid batch accepted.");
        }
        catch (ArgumentException) { }
        if ((await database.GetStatsAsync(cancellation)).Tracks != 0)
            throw new InvalidOperationException("Invalid batch partially committed.");
        await database.FinishScanAsync(job, ScanStatus.Failed, 0, false, "invalid_batch", cancellation);
        var flac = Path.Combine(root, "valid.flac");
        AudioFixture.Flac(flac);
        var flacMetadata = await reader.ReadAsync(AudioFixture.Snapshot(flac), cancellation);
        if (flacMetadata.Title != "无损 测试" || flacMetadata.Artist != "FLAC Artist" || flacMetadata.BitDepth != 24)
            throw new InvalidOperationException("FLAC tags changed.");
        File.WriteAllText(Path.Combine(root, "stub.osu"), "osu file format v14");
        var normal = new ScanCoordinator(database, new DirectoryDiscovery(), () => new LocalMetadataReader());
        var virtualReport = await normal.ScanAsync(new(root), cancellation: cancellation);
        var virtualIssues = await database.GetScanIssuesAsync(virtualReport.JobId, cancellation: cancellation);
        if (virtualReport.Failed != 1 || virtualIssues.Count != 1 || virtualIssues[0].Code != "virtual_tracks_not_migrated")
            throw new InvalidOperationException("Unmigrated virtual tracks were silently ignored.");
        var incremental = await normal.ScanAsync(new(root), cancellation: cancellation);
        if (incremental.Unchanged != 2 || incremental.Failed != 1 || incremental.Updated != 0)
            throw new InvalidOperationException("Incremental identity changed.");
        if (OperatingSystem.IsWindows())
        {
            var caseJob = await database.BeginScanAsync(root, false, cancellation);
            var snapshot = AudioFixture.Snapshot(file);
            var known = await database.FindKnownAsync([snapshot], cancellation);
            var item = new ScanEntry(snapshot, ScanEntryKind.Metadata, metadata, known[snapshot.PathKey].Id);
            await database.CommitScanBatchAsync(caseJob, 1, [item], cancellation);
            try
            {
                await database.CommitScanBatchAsync(caseJob, 2,
                    [item with { File = snapshot with { Path = snapshot.Path.ToLowerInvariant() } }], cancellation);
                throw new InvalidOperationException("Ambiguous path silently overwrote identity.");
            }
            catch (MetadataReadException error) when (error.Code == "path_case_identity_required") { }
            await database.FinishScanAsync(caseJob, ScanStatus.Failed, 1, false, "path_case_identity_required", cancellation);
            return 7;
        }
        return 6;
    }

    private sealed class ChangingReader : IMetadataReader
    {
        private readonly LocalMetadataReader _reader = new();
        public async ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, CancellationToken cancellation)
        {
            var value = await _reader.ReadAsync(file, cancellation);
            File.SetLastWriteTimeUtc(file.Path, DateTime.UtcNow.AddMinutes(1));
            return value;
        }
        public ValueTask DisposeAsync() => _reader.DisposeAsync();
    }
}
