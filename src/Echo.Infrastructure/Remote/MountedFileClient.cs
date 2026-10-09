using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

// SMB/CIFS and SSHFS use an explicitly selected OS mount, as in the original ECHO.
internal sealed class MountedFileClient : IRemoteFileClient
{
    private readonly SemaphoreSlim admission = new(2, 2);
    public async Task<RemoteDirectoryPage> BrowseAsync(RemoteConnection source, string path, int page, string search, bool audioOnly, CancellationToken token)
    {
        RemoteBrowseRules.Request(path, page, search);
        return RemoteBrowseRules.Page(await ListChildrenAsync(source, path, token).ConfigureAwait(false), page, search, audioOnly);
    }
    public string? LocalPath(RemoteConnection source, string path)
    {
        try { return Resolve(source.BaseUrl, path, CancellationToken.None); }
        catch (Exception) { return null; }
    }
    public async Task<IReadOnlyList<RemoteEntry>> ListChildrenAsync(RemoteConnection source, string path, CancellationToken token)
    {
        RemoteBrowseRules.PathParts(path);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(15));
        var workToken = budget.Token;
        await admission.WaitAsync(budget.Token).ConfigureAwait(false);
        // An unresponsive OS mount can block a syscall. Retain its admission lease until it returns.
        var worker = Task.Run(() =>
        {
            try
            {
                var directory = Resolve(source.BaseUrl, path, workToken); var rows = new List<RemoteEntry>(); var count = 0;
                foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    workToken.ThrowIfCancellationRequested();
                    if (++count > RemoteBrowseRules.MaxEntries) throw new IOException("挂载目录超过 20,000 项，请选择较小的音乐目录。");
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var folder = item is DirectoryInfo;
                    var remote = path.TrimEnd('/') + "/" + item.Name + (folder ? "/" : "");
                    rows.Add(new(remote, item.Name, folder, !folder && RemoteBrowseRules.Audio(item.Name),
                        item is FileInfo file ? file.Length : null, item.LastWriteTimeUtc.ToString("yyyy-MM-dd")));
                }
                return (IReadOnlyList<RemoteEntry>)rows;
            }
            finally { admission.Release(); }
        }, CancellationToken.None);
        _ = worker.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await worker.WaitAsync(budget.Token).ConfigureAwait(false);
    }
    public async Task<byte[]?> ReadFileAsync(RemoteConnection source, string path, int maxBytes, CancellationToken token)
    {
        var full = LocalPath(source, path);
        if (full is null || !File.Exists(full)) return null;
        var length = new FileInfo(full).Length;
        if (length <= 0 || length > maxBytes) return null;
        return await File.ReadAllBytesAsync(full, token).ConfigureAwait(false);
    }
    public async Task<RemoteMediaSource> ResolveMediaAsync(RemoteConnection source, RemoteEntry entry, CancellationToken token)
    {
        await admission.WaitAsync(token).ConfigureAwait(false);
        var worker = Task.Run(() =>
        {
            try
            {
                var path = Resolve(source.BaseUrl, entry.Path, token);
                if (!File.Exists(path) || !RemoteBrowseRules.Audio(path)) throw new IOException("挂载目录中的音频文件不可访问。");
                return new RemoteMediaSource("local", path);
            }
            finally { admission.Release(); }
        }, CancellationToken.None);
        _ = worker.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await worker.WaitAsync(token).ConfigureAwait(false);
    }
    private static string Resolve(string root, string path, CancellationToken token)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(fullRoot) || fullRoot.StartsWith("\\\\.\\", StringComparison.Ordinal)
            || fullRoot.Contains("GLOBALROOT", StringComparison.OrdinalIgnoreCase)) throw new IOException("系统挂载目录不可访问，请先连接共享盘。");
        var parts = RemoteBrowseRules.PathParts(path); var current = fullRoot;
        foreach (var part in parts)
        {
            token.ThrowIfCancellationRequested();
            if (part.Contains(':') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("挂载目录路径无效。");
            current = Path.GetFullPath(Path.Combine(current, part));
            if (!Path.GetRelativePath(fullRoot, current).Split(Path.DirectorySeparatorChar).All(x => x != "..")) throw new ArgumentException("路径超出了授权目录。");
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("不能进入挂载目录中的符号链接或目录联接。");
        }
        return current;
    }
}
