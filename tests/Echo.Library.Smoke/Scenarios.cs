using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;
using Microsoft.Data.Sqlite;

internal static class Scenarios
{
    public static async Task<int> RunAsync(string root, string? native, CancellationToken cancellation)
    {
        var media = Path.Combine(root, "media");
        Directory.CreateDirectory(media);
        for (var i = 0; i < 24; i++) AudioFixture.Wave(Path.Combine(media, $"track-{i:000}.wav"), $"Track {i:000}");
        AudioFixture.Flac(Path.Combine(media, "中文.flac"));
        var path = Path.Combine(root, "scan.sqlite");
        var checks = 0;
        void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("FAILED: " + label); checks++; }

        await using (var db = await LibraryDatabase.OpenAsync(path, cancellation))
        {
            var scan = new ScanCoordinator(db, new DirectoryDiscovery(), () => new LocalMetadataReader(native));
            var first = await scan.ScanAsync(new(media), cancellation: cancellation);
            Check(first.Status == ScanStatus.Completed && first.Inserted == 25 && first.Failed == 0, "real WAV/FLAC import");
            var page = await db.GetTracksAsync(new(10), cancellation);
            Check(page.Items.Count == 10 && page.Next is not null && page.Items[0].DurationSeconds > 0, "bounded page");
            var secondPage = await db.GetTracksAsync(new(10, After: page.Next), cancellation);
            Check(!page.Items.Select(x => x.Id).Intersect(secondPage.Items.Select(x => x.Id)).Any(), "cursor tie/order");
            var search = await db.GetTracksAsync(new(10, "FLAC"), cancellation);
            Check(search.Items.Count == 1 && search.Items[0].BitDepth == 24 && search.Items[0].Artist == "FLAC Artist", "FTS/tag properties");

            var revision = (await db.GetStatsAsync(cancellation)).Revision;
            var incremental = await scan.ScanAsync(new(media), cancellation: cancellation);
            Check(incremental.Unchanged == 25 && incremental.Inserted == 0 && incremental.Updated == 0 &&
                (await db.GetStatsAsync(cancellation)).Revision == revision, "unchanged skips parse and content writes");
            AudioFixture.Wave(Path.Combine(media, "track-000.wav"), "Changed title");
            File.SetLastWriteTimeUtc(Path.Combine(media, "track-000.wav"), DateTime.UtcNow.AddSeconds(-3));
            var changed = await scan.ScanAsync(new(media), cancellation: cancellation);
            Check(changed.Updated == 1 && changed.Unchanged == 24, "changed-only import");
            try { await db.GetTracksAsync(new(10, After: page.Next), cancellation); Check(false, "stale cursor"); }
            catch (LibraryChangedException) { checks++; }

            File.Delete(Path.Combine(media, "track-001.wav"));
            File.WriteAllText(Path.Combine(media, "bad.wav"), "not audio");
            var partial = await scan.ScanAsync(new(media, DetectMissing: true), cancellation: cancellation);
            Check(partial.Failed == 1 && partial.MarkedMissing == 0 &&
                (await db.GetStatsAsync(cancellation)).Missing == 0, "bad file never permits missing cleanup");
            File.Delete(Path.Combine(media, "bad.wav"));
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var slow = new ScanCoordinator(db, new DirectoryDiscovery(), () => new DelayedReader(started, native));
            var pending = slow.ScanAsync(new(media, Force: true, DetectMissing: true), cancellation: stop.Token);
            await started.Task.WaitAsync(cancellation); stop.Cancel();
            var cancelled = await pending;
            Check(cancelled.Status == ScanStatus.Cancelled && cancelled.MarkedMissing == 0, "cancellation retains known data");
            var completed = await scan.ScanAsync(new(media, DetectMissing: true), cancellation: cancellation);
            Check(completed.MarkedMissing == 1 && (await db.GetStatsAsync(cancellation)).Missing == 1, "complete root missing reconciliation");

            var job = await db.BeginScanAsync(media, false, cancellation);
            var file = AudioFixture.Snapshot(Path.Combine(media, "track-000.wav"));
            await using var metadata = new LocalMetadataReader(native);
            var value = await metadata.ReadAsync(file, cancellation);
            var entry = new ScanEntry(file, ScanEntryKind.Metadata, value, page.Items[0].Id);
            var receipt = await db.CommitScanBatchAsync(job, 1, [entry], cancellation);
            var duplicate = await db.CommitScanBatchAsync(job, 1, [entry], cancellation);
            Check(receipt == duplicate, "same batch receipt is idempotent");
            try
            {
                await db.CommitScanBatchAsync(job, 1, [entry with { Metadata = value with { Title = "different" } }], cancellation);
                Check(false, "conflicting replay");
            }
            catch (InvalidOperationException) { checks++; }
            await db.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, cancellation);
            try { await using var duplicateOwner = await LibraryDatabase.OpenAsync(path, cancellation); Check(false, "writer ownership"); }
            catch (IOException) { checks++; }

            if (native is not null)
            {
                await using var cpp = new NativeMetadataReader(native);
                var nativeValue = await cpp.ReadAsync(file, cancellation);
                Check(nativeValue.Title == value.Title && nativeValue.Artist == value.Artist &&
                    Math.Abs(nativeValue.DurationSeconds - value.DurationSeconds) < 0.000001, "C++ protocol and Unicode parity");
            }
        }

        await using (var reopened = await LibraryDatabase.OpenAsync(path, cancellation))
            Check((await reopened.GetStatsAsync(cancellation)).Tracks == 25, "persisted reopen");
        var foreign = Path.Combine(root, "legacy.sqlite");
        using (var other = new SqliteConnection("Data Source=" + foreign))
        {
            other.Open(); using var command = other.CreateCommand(); command.CommandText = "CREATE TABLE legacy(value TEXT)"; command.ExecuteNonQuery();
        }
        try { await using var rejected = await LibraryDatabase.OpenAsync(foreign, cancellation); Check(false, "foreign schema"); }
        catch (InvalidOperationException) { checks++; }
        return checks;
    }

    private sealed class DelayedReader(TaskCompletionSource started, string? native) : IMetadataReader
    {
        private readonly LocalMetadataReader _inner = new(native);
        public async ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, CancellationToken cancellation)
        {
            started.TrySetResult();
            await Task.Delay(100, cancellation);
            return await _inner.ReadAsync(file, cancellation);
        }
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
