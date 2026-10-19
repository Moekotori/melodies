using System.Threading.Channels;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Core.Library;

// One coalesced background scan consumer. Callbacks retain root IDs, never file contents or a whole tree.
internal sealed class FolderWatchCoordinator : IAsyncDisposable
{
    private readonly Func<FolderScanOptions, IProgress<ScanProgress>, CancellationToken, Task<ScanReport>> _scan;
    private readonly Action<string> _changed;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<string> _pending = Channel.CreateBounded<string>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private bool _disposed;
    public int Count { get { lock (_watchers) return _watchers.Count; } }
    public FolderWatchCoordinator(Func<FolderScanOptions, IProgress<ScanProgress>, CancellationToken, Task<ScanReport>> scan, Action<string> changed)
    { _scan = scan; _changed = changed; _worker = Task.Run(WorkAsync); }
    public Task SetAsync(string path, bool enabled, CancellationToken cancellation) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested(); path = Path.GetFullPath(path);
        lock (_watchers)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watchers.Remove(path, out var old)) old.Dispose();
            if (!enabled) return;
            if (_watchers.Count >= 16) throw new InvalidOperationException("最多同时监听 16 个根目录。");
            var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 16384 };
            watcher.Created += Notify; watcher.Changed += Notify; watcher.Deleted += Notify; watcher.Renamed += (_, _) => _pending.Writer.TryWrite(path);
            watcher.Error += (_, _) => _pending.Writer.TryWrite(path);
            _watchers[path] = watcher; watcher.EnableRaisingEvents = true;
            void Notify(object sender, FileSystemEventArgs args)
            {
                if (Path.GetExtension(args.FullPath) is ".sqlite" or ".writer.lock") return;
                _pending.Writer.TryWrite(path);
            }
        }
    }, cancellation);
    private async Task WorkAsync()
    {
        try
        {
            while (await _pending.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (_pending.Reader.TryRead(out var path)) roots.Add(path);
                await Task.Delay(1200, _stop.Token).ConfigureAwait(false);
                while (_pending.Reader.TryRead(out var path)) roots.Add(path);
                foreach (var root in roots.Take(16))
                {
                    lock (_watchers) { if (!_watchers.ContainsKey(root)) continue; }
                    try
                    {
                        var report = await _scan(new(root, FolderScanMode.Full), new Progress<ScanProgress>(), _stop.Token).ConfigureAwait(false);
                        if (report.Status == ScanStatus.Completed) _changed(root);
                    }
                    catch (InvalidOperationException) { _pending.Writer.TryWrite(root); await Task.Delay(1500, _stop.Token).ConfigureAwait(false); }
                    catch (Exception) when (!_stop.IsCancellationRequested) { /* A manual rescan exposes file errors without a background notification flood. */ }
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _pending.Writer.TryComplete(); lock (_watchers) { _disposed = true; foreach (var watcher in _watchers.Values) watcher.Dispose(); _watchers.Clear(); }
        await _worker.ConfigureAwait(false); _stop.Dispose();
    }
}
