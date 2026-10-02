using System.Diagnostics;
using System.Threading.Channels;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Application.Scanning;

public sealed class ScanCoordinator(ILibraryStore store, IFileDiscovery discovery, Func<IMetadataReader> readerFactory)
{
    private int _active;

    public async Task<ScanReport> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        if (request.Workers is < 1 or > 4 || request.BatchSize is < 1 or > 256)
            throw new ArgumentException("Workers must be 1..4; batch size must be 1..256.");
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            throw new InvalidOperationException("A scan is already active.");
        try { return await RunAsync(request, progress, cancellation).ConfigureAwait(false); }
        finally { Volatile.Write(ref _active, 0); }
    }

    private async Task<ScanReport> RunAsync(ScanRequest request, IProgress<ScanProgress>? progress,
        CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew();
        var job = await store.BeginScanAsync(request.Root, request.DetectMissing, cancellation).ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var files = Channel.CreateBounded<FileSnapshot[]>(new BoundedChannelOptions(2)
        { SingleWriter = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        var results = Channel.CreateBounded<ScanEntry>(new BoundedChannelOptions(request.BatchSize)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        var counters = new ScanCounters(job, watch, progress);
        counters.Publish("discovering", true);
        Exception? failure = null;

        async Task Guard(Func<Task> action)
        {
            try { await action().ConfigureAwait(false); }
            catch (Exception error)
            {
                if (error is not OperationCanceledException) Interlocked.CompareExchange(ref failure, error, null);
                await stop.CancelAsync().ConfigureAwait(false);
                throw;
            }
        }

        var discover = Task.Run(() => Guard(async () =>
        {
            Exception? error = null;
            try
            {
                var batch = new List<FileSnapshot>(request.BatchSize);
                var characters = 0;
                foreach (var file in discovery.Enumerate(request.Root, request.DetectMissing, stop.Token, request.Force))
                {
                    var size = file.Path.Length + file.PathKey.Length;
                    if (batch.Count != 0 && (batch.Count == request.BatchSize || characters + size > 256 * 1024))
                    {
                        await files.Writer.WriteAsync(batch.ToArray(), stop.Token).ConfigureAwait(false);
                        counters.Publish("discovering");
                        batch.Clear(); characters = 0;
                    }
                    batch.Add(file); characters += size;
                    Interlocked.Increment(ref counters.Discovered);
                }
                if (batch.Count != 0) await files.Writer.WriteAsync(batch.ToArray(), stop.Token).ConfigureAwait(false);
            }
            catch (Exception caught) { error = caught; throw; }
            finally { files.Writer.TryComplete(error); }
        }), CancellationToken.None);

        var workers = Enumerable.Range(0, request.Workers).Select(_ => Task.Run(() => Guard(async () =>
        {
            await using var reader = readerFactory();
            await foreach (var batch in files.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                var known = await store.FindKnownAsync(batch, stop.Token).ConfigureAwait(false);
                foreach (var file in batch)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    known.TryGetValue(file.PathKey, out var existing);
                    ScanEntry entry;
                    if (file.Issue is { } issue)
                        entry = new(file, ScanEntryKind.Error, ExistingId: existing?.Id, ErrorCode: issue);
                    else if (file.Unavailable)
                        entry = new(file, ScanEntryKind.Error, ExistingId: existing?.Id, ErrorCode: "cloud_file_unavailable");
                    else if (!request.Force && existing is { Missing: false, ParserVersion: TrackMetadata.ParserVersion } &&
                        StringComparer.Ordinal.Equals(existing.Path, file.Path) &&
                        existing.DependencyStamp == file.Source?.Stamp &&
                        existing.SizeBytes == file.SizeBytes && existing.ModifiedTicks == file.ModifiedTicks)
                        entry = new(file, ScanEntryKind.Unchanged, ExistingId: existing.Id);
                    else
                        entry = await ReadChangedAsync(reader, file, existing?.Id, stop.Token).ConfigureAwait(false);
                    await results.Writer.WriteAsync(entry, stop.Token).ConfigureAwait(false);
                }
            }
        }), CancellationToken.None)).ToArray();

        async Task CompleteResults()
        {
            try { await Task.WhenAll(workers).ConfigureAwait(false); }
            finally { results.Writer.TryComplete(failure); }
        }
        var complete = CompleteResults();
        var commit = Task.Run(() => Guard(() =>
            ScanCommitPump.RunAsync(store, job, results.Reader, request.BatchSize, counters, stop.Token)), CancellationToken.None);
        ScanStatus status;
        string? errorCode = null;
        try
        {
            await Task.WhenAll(discover, complete, commit).ConfigureAwait(false);
            status = ScanStatus.Completed;
        }
        catch
        {
            status = cancellation.IsCancellationRequested ? ScanStatus.Cancelled : ScanStatus.Failed;
            errorCode = status == ScanStatus.Cancelled ? "cancelled" : failure switch
            {
                UnauthorizedAccessException => "directory_access_denied",
                MetadataReadException metadataError => metadataError.Code,
                IOException => "scan_io_failed",
                _ => "scan_pipeline_failed"
            };
        }
        var missing = await store.FinishScanAsync(job, status, Interlocked.Read(ref counters.Discovered),
            request.DetectMissing, errorCode, CancellationToken.None).ConfigureAwait(false);
        counters.Publish("finished", true);
        return new(job, status, counters.Discovered, counters.Inserted, counters.Updated,
            counters.Unchanged, counters.Failed, missing, watch.Elapsed.TotalMilliseconds, errorCode);
    }

    private static async ValueTask<ScanEntry> ReadChangedAsync(IMetadataReader reader, FileSnapshot file,
        string? existingId, CancellationToken cancellation)
    {
        try
        {
            var metadata = await reader.ReadAsync(file, cancellation).ConfigureAwait(false);
            // A tag/file changing mid-read cannot produce a falsely up-to-date fingerprint.
            var after = new FileInfo(file.Source?.AudioPath ?? file.Path);
            if (!after.Exists || after.Length != (file.Source?.AudioSize ?? file.SizeBytes) ||
                after.LastWriteTimeUtc.Ticks != (file.Source?.AudioTicks ?? file.ModifiedTicks))
                return new(file, ScanEntryKind.Error, ExistingId: existingId, ErrorCode: "file_changed_during_scan");
            if (file.Source is { } source)
            {
                var definition = new FileInfo(source.DefinitionPath);
                if (!definition.Exists || definition.Length != source.DefinitionSize || definition.LastWriteTimeUtc.Ticks != source.DefinitionTicks)
                    return new(file, ScanEntryKind.Error, ExistingId: existingId, ErrorCode: "source_changed_during_scan");
            }
            return new(file, ScanEntryKind.Metadata, metadata, existingId);
        }
        catch (MetadataReadException error) { return new(file, ScanEntryKind.Error, ExistingId: existingId, ErrorCode: error.Code); }
        catch (IOException) { return new(file, ScanEntryKind.Error, ExistingId: existingId, ErrorCode: "metadata_io_failed"); }
        catch (UnauthorizedAccessException) { return new(file, ScanEntryKind.Error, ExistingId: existingId, ErrorCode: "file_access_denied"); }
        catch (OverflowException) { return new(file, ScanEntryKind.Error, ExistingId: existingId, ErrorCode: "invalid_audio_size"); }
    }
}
