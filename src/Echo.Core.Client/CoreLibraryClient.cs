using Echo.Core.Contracts;
using Echo.Core.Transport;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Core.Client;

public sealed partial class CoreLibraryClient : ICoreLibrary, Echo.Contracts.Remote.IRemoteIndexSync, ICoreTaskControl, ICoreBatchImport, IArtistImportNotifications
{
    private readonly CoreWireClient wire;
    private Guid activeTask;
    public event Action<string>? FolderChanged;
    public event Action<ArtistImportSettings>? ArtistImportChanged;
    internal CoreLibraryClient(CoreWireClient wire)
    {
        this.wire = wire;
        wire.Received += message =>
        {
            if (message.Method == "library.folder") FolderChanged?.Invoke(CoreJson.Read<string>(message.Payload));
            if (message.Method == "library.artistImport") ArtistImportChanged?.Invoke(CoreJson.Read<ArtistImportSettings>(message.Payload));
        };
    }

    public async Task<ScanReport> ImportAsync(string folder, IProgress<ScanProgress> progress, CancellationToken cancellation)
        => (await RunAsync(new(folder), progress, null, cancellation).ConfigureAwait(false)).Report
            ?? throw new IOException("Core 扫描没有返回报告。");
    public async Task<ScanReport> ScanFolderAsync(FolderScanOptions options, IProgress<ScanProgress> progress, CancellationToken cancellation)
        => (await RunAsync(new(options.Path, options), progress, null, cancellation).ConfigureAwait(false)).Report
            ?? throw new IOException("Core 扫描没有返回报告。");
    public async Task CheckFolderResourcesAsync(FolderTrackQuery query, IProgress<FolderResourceProgress> progress, CancellationToken cancellation)
        => _ = await RunAsync(new("", Resources: query), null, progress, cancellation).ConfigureAwait(false);

    public async Task<Echo.Contracts.Remote.RemoteSyncResult> SyncSourceAsync(string sourceId, IProgress<Echo.Contracts.Remote.RemoteSyncProgress> progress, CancellationToken token)
        => (await RunAsync(new("", RemoteSourceId: sourceId), null, null, token, progress).ConfigureAwait(false)).RemoteResult ?? throw new IOException("Core 没有返回索引报告。");
    public async Task<ScanReport> ScanAsync(ScanRequest request, string? scanner, CancellationToken token = default)
        => (await RunAsync(new(request.Root, Scan: request, Scanner: scanner), null, null, token).ConfigureAwait(false)).Report ?? throw new IOException("Core 没有返回扫描报告。");
    public async Task<ScanReport> ImportBatchAsync(IReadOnlyList<string> paths, IProgress<CoreTaskSnapshot> progress, CancellationToken token)
        => (await RunAsync(new("", Paths: paths), null, null, token, status: progress).ConfigureAwait(false)).Report ?? throw new IOException("Core 没有返回导入报告。");
    public Task CancelActiveTaskAsync(CancellationToken token = default)
        => activeTask == Guid.Empty ? Task.CompletedTask : wire.CallAsync("tasks.cancel", CoreJson.Value(new CoreTaskCancel(activeTask)), token);
    public Task<int> SetAllFolderWatchAsync(bool enabled, CancellationToken token)
        => wire.CallAsync<int>("settings.watchAll", CoreJson.Value(enabled), token);
    public async Task RunMaintenanceAsync(string action, IProgress<CoreTaskSnapshot> progress, CancellationToken token)
        => _ = await RunAsync(new("", Maintenance: action), null, null, token, status: progress).ConfigureAwait(false);
    public Task<CoreTaskStart> StartTaskAsync(CoreTaskRequest request, CancellationToken token = default)
    {
        activeTask = request.Id == Guid.Empty ? Guid.NewGuid() : request.Id;
        return wire.CallAsync<CoreTaskStart>("tasks.start", CoreJson.Value(request with { Id = activeTask }), token);
    }
    public Task<CoreTaskSnapshot[]> TasksAsync(CancellationToken token = default)
        => wire.CallAsync<CoreTaskSnapshot[]>("tasks.list", CoreJson.Empty, token);
    public Task<CoreTaskSnapshot> GetTaskAsync(Guid id, CancellationToken token = default)
        => wire.CallAsync<CoreTaskSnapshot>("tasks.get", CoreJson.Value(new CoreTaskCancel(id)), token);
    public Task CancelTaskAsync(Guid id, CancellationToken token = default)
        => wire.CallAsync("tasks.cancel", CoreJson.Value(new CoreTaskCancel(id)), token);

    private async Task<CoreTaskSnapshot> RunAsync(CoreTaskRequest request, IProgress<ScanProgress>? scan,
        IProgress<FolderResourceProgress>? resources, CancellationToken token, IProgress<Echo.Contracts.Remote.RemoteSyncProgress>? remote = null, IProgress<CoreTaskSnapshot>? status = null)
    {
        var accepted = await StartTaskAsync(request, token).ConfigureAwait(false);
        return await CoreTaskAwaiter.WaitAsync(wire, accepted.Id, state =>
        {
            status?.Report(state);
            if (state.ScanProgress is { } scanProgress) scan?.Report(scanProgress);
            if (state.ResourceProgress is { } resourceProgress) resources?.Report(resourceProgress);
            if (state.RemoteProgress is { } remoteProgress) remote?.Report(remoteProgress);
        }, token).ConfigureAwait(false);
    }
}
