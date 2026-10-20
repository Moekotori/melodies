using Echo.Contracts.Scanning;
using Echo.Contracts.Library;
using Echo.Core.Contracts;

namespace Echo.Core.Hosting;

/// <summary>Accepted work belongs to Core, not to the connection that submitted it.</summary>
internal sealed class CoreTaskRegistry(CoreRuntime runtime) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    private readonly CancellationTokenSource stop = new();
    private bool closed;
    public bool HasActive { get { lock (gate) return entries.Values.Any(value => !value.Work.IsCompleted); } }

    public CoreTaskStart Start(CoreTaskRequest request)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (request.Paths is { Count: > 256 }) throw new ArgumentException("一次最多导入 256 项。");
            if (request.Id != Guid.Empty && entries.ContainsKey(request.Id)) return new(request.Id);
            if (entries.Values.Any(value => !value.Work.IsCompleted)) throw new InvalidOperationException("已有 Core 任务正在运行，请等待完成或显式取消。");
            while (entries.Count >= 16)
            {
                var retired = entries.First(pair => pair.Value.Work.IsCompleted); entries.Remove(retired.Key); retired.Value.Cancel.Dispose();
            }
            var id = request.Id == Guid.Empty ? Guid.NewGuid() : request.Id;
            if (entries.ContainsKey(id)) return new(id);
            var entry = new Entry(new(id, request.ArtistImport is not null ? "artist-import" : request.RemoteSourceId is not null ? "remote" : request.Resources is null ? "scan" : "resources", "running"), stop.Token);
            entries[id] = entry;
            entry.Work = Task.Run(async () =>
            {
                try
                {
                    if (request.ArtistImport is { } artistImport)
                    {
                        await runtime.Library.RebuildArtistCreditsAsync(artistImport,
                            new DirectProgress<ArtistImportProgress>(progress => Update(id, value => value with { ArtistImportProgress = progress })), entry.Cancel.Token).ConfigureAwait(false);
                        Update(id, value => value with { Status = "completed" });
                    }
                    else if (request.BackupPath is { } destination)
                    {
                        await runtime.Backup.CreateAsync(destination, entry.Cancel.Token).ConfigureAwait(false);
                        Update(id, value => value with { Status = "completed" });
                    }
                    else if (request.Paths is { Count: > 0 } paths)
                    {
                        ScanReport? total = null;
                        for (var index = 0; index < paths.Count; index++)
                        {
                            var ordinal = index + 1;
                            var report = await runtime.Library.ImportAsync(paths[index], new DirectProgress<ScanProgress>(progress =>
                                Update(id, value => value with { ScanProgress = progress, InputIndex = ordinal, InputCount = paths.Count })), entry.Cancel.Token).ConfigureAwait(false);
                            total = total is null ? report : total with { Discovered = total.Discovered + report.Discovered, Inserted = total.Inserted + report.Inserted,
                                Updated = total.Updated + report.Updated, Unchanged = total.Unchanged + report.Unchanged, Failed = total.Failed + report.Failed,
                                MarkedMissing = total.MarkedMissing + report.MarkedMissing, ElapsedMilliseconds = total.ElapsedMilliseconds + report.ElapsedMilliseconds };
                            if (report.Status != ScanStatus.Completed) throw new IOException("导入未完成，请查看 Core 扫描记录。");
                        }
                        Update(id, value => value with { Status = "completed", Report = total });
                    }
                    else if (request.Maintenance is { } action)
                    {
                        var roots = await runtime.Library.GetFoldersAsync(new(Limit: 64), entry.Cancel.Token).ConfigureAwait(false);
                        if (roots.HasMore) throw new InvalidOperationException("维护最多处理 64 个根目录。");
                        foreach (var root in roots.Items)
                        {
                            if (action == "检查封面与歌词")
                                await runtime.Library.CheckFolderResourcesAsync(new(root.Path), new DirectProgress<FolderResourceProgress>(progress => Update(id, value => value with { ResourceProgress = progress })), entry.Cancel.Token).ConfigureAwait(false);
                            else if (action == "重新读取全部歌曲")
                            {
                                var report = await runtime.Library.ScanFolderAsync(new(root.Path, FolderScanMode.EmbeddedTags), new DirectProgress<ScanProgress>(progress => Update(id, value => value with { ScanProgress = progress })), entry.Cancel.Token).ConfigureAwait(false);
                                if (report.Status != ScanStatus.Completed) throw new IOException("资料重读未完成。");
                            }
                            else throw new ArgumentException("不支持的维护动作。");
                        }
                        Update(id, value => value with { Status = "completed" });
                    }
                    else if (request.RemoteSourceId is { } sourceId)
                    {
                        var sync = new Echo.Application.Remote.RemoteLibrarySync(runtime.RemoteSources, runtime.RemoteFiles, runtime.Library);
                        var result = await sync.RunAsync(sourceId, new DirectProgress<Echo.Contracts.Remote.RemoteSyncProgress>(progress => Update(id, value => value with { RemoteProgress = progress })), entry.Cancel.Token).ConfigureAwait(false);
                        Update(id, value => value with { Status = "completed", RemoteResult = result });
                    }
                    else if (request.Resources is { } resources)
                    {
                        await runtime.Library.CheckFolderResourcesAsync(resources,
                            new DirectProgress<FolderResourceProgress>(progress => Update(id, value => value with { ResourceProgress = progress })), entry.Cancel.Token).ConfigureAwait(false);
                        Update(id, value => value with { Status = "completed" });
                    }
                    else
                    {
                        var progress = new DirectProgress<ScanProgress>(value => Update(id, state => state with { ScanProgress = value }));
                        var report = request.Scan is { } scanRequest
                            ? await runtime.Library.ScanRequestAsync(scanRequest, request.Scanner, progress, entry.Cancel.Token).ConfigureAwait(false)
                            : request.Options is { } options
                            ? await runtime.Library.ScanFolderAsync(options, progress, entry.Cancel.Token).ConfigureAwait(false)
                            : await runtime.Library.ImportAsync(request.Path, progress, entry.Cancel.Token).ConfigureAwait(false);
                        Update(id, value => value with { Report = report, Status = report.Status == ScanStatus.Completed ? "completed" : report.Status == ScanStatus.Cancelled ? "canceled" : "failed", Error = report.ErrorCode });
                    }
                }
                catch (OperationCanceledException) { Update(id, value => value with { Status = "canceled" }); }
                catch (ArtistImportChangedException error) { Update(id, value => value with { Status = "failed", Error = error.Message }); }
                catch (Exception) { Update(id, value => value with { Status = "failed", Error = "Core 任务未完成，请检查文件或权限。" }); }
            });
            return new(id);
        }
    }

    public CoreTaskSnapshot Get(Guid id) { lock (gate) return entries.TryGetValue(id, out var value) ? value.State : throw new ArgumentException("Core 任务已过期。"); }
    public CoreTaskSnapshot[] List() { lock (gate) return entries.Values.Select(entry => entry.State).ToArray(); }
    public void Cancel(Guid id) { lock (gate) if (entries.TryGetValue(id, out var value) && !value.Work.IsCompleted) value.Cancel.Cancel(); }
    private void Update(Guid id, Func<CoreTaskSnapshot, CoreTaskSnapshot> change)
    { lock (gate) if (entries.TryGetValue(id, out var entry)) entry.State = change(entry.State); }

    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (gate) { closed = true; stop.Cancel(); tasks = entries.Values.Select(entry => entry.Work).ToArray(); }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var entry in entries.Values) entry.Cancel.Dispose();
        stop.Dispose();
    }
    private sealed class Entry(CoreTaskSnapshot state, CancellationToken token)
    {
        public CoreTaskSnapshot State = state;
        public readonly CancellationTokenSource Cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        public Task Work = Task.CompletedTask;
    }
    private sealed class DirectProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
