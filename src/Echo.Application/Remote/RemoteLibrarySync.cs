using System.Threading.Channels;
using Echo.Contracts.Remote;

namespace Echo.Application.Remote;

// Two directories in flight, matching the HTTP admission of two. One network walk writes the library.
public sealed class RemoteLibrarySync(RemoteSourceCatalog catalog, IRemoteFileClient files, IRemoteTrackIndex index)
{
    public const int MaxTracks = 100_000, MaxDirectories = 20_000;
    public async Task<RemoteSyncResult> RunAsync(string sourceId, IProgress<RemoteSyncProgress>? progress, CancellationToken token)
    {
        var source = catalog.GetConnection(sourceId);
        if (source.SyncMode != "index") throw new InvalidOperationException("这个来源只浏览和播放，不会写入曲库。");
        var pages = files.ReadLibraryPagesAsync(source, index, token);
        if (pages is not null) return await SubsonicLibrarySync.RunAsync(source.Id, pages, index, progress, token).ConfigureAwait(false);
        var root = string.IsNullOrWhiteSpace(source.RootPath) ? "/" : source.RootPath;
        if (!root.StartsWith('/')) root = "/" + root;
        root = root.EndsWith('/') || root == "/" ? root : root + "/";
        var syncToken = Guid.NewGuid().ToString("N");
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var batch = new List<RemoteIndexTrack>(64);
        var gate = new object();
        var flush = new SemaphoreSlim(1, 1);
        var inserted = 0; var updated = 0; var unchanged = 0; var discovered = 0; var failed = 0;
        var inflight = 0;
        using var crawl = CancellationTokenSource.CreateLinkedTokenSource(token);
        void Admit(string path)
        {
            if (!Inside(root, path)) return;
            lock (gate)
            {
                if (!seen.Add(path)) return;
                if (seen.Count > MaxDirectories) throw new IOException("目录数量超过 20,000，请缩小同步范围。");
                inflight++;
            }
            if (!channel.Writer.TryWrite(path)) throw new IOException("目录队列已满，请缩小同步范围。");
        }
        async Task CommitAsync(RemoteIndexTrack[] ready)
        {
            await flush.WaitAsync(crawl.Token).ConfigureAwait(false);
            try
            {
                var wrote = await index.WriteAsync(ready, syncToken, crawl.Token).ConfigureAwait(false);
                int found, added, changed, same, directories;
                lock (gate)
                {
                    inserted += wrote.Inserted; updated += wrote.Updated; unchanged += wrote.Unchanged;
                    found = discovered; added = inserted; changed = updated; same = unchanged; directories = seen.Count;
                }
                progress?.Report(new(directories, found, added, changed, same));
            }
            finally { flush.Release(); }
        }
        async Task OfferAsync(RemoteIndexTrack track)
        {
            RemoteIndexTrack[]? ready = null;
            lock (gate)
            {
                if (++discovered > MaxTracks) throw new IOException("曲目超过 100,000 首，请缩小音乐目录后再索引。");
                batch.Add(track);
                if (batch.Count == 64) { ready = batch.ToArray(); batch.Clear(); }
            }
            if (ready is not null) await CommitAsync(ready).ConfigureAwait(false);
        }
        async Task VisitAsync(string path)
        {
            var children = await files.ListChildrenAsync(source, path, crawl.Token).ConfigureAwait(false);
            foreach (var entry in children)
            {
                crawl.Token.ThrowIfCancellationRequested();
                if (entry.IsDirectory) Admit(entry.Path);
                else if (entry.IsAudio) await OfferAsync(ToTrack(source.Id, entry)).ConfigureAwait(false);
            }
            int found, added, changed, same, directories;
            lock (gate) { found = discovered; added = inserted; changed = updated; same = unchanged; directories = seen.Count; }
            progress?.Report(new(directories, found, added, changed, same));
        }
        async Task WorkerAsync()
        {
            await foreach (var path in channel.Reader.ReadAllAsync(crawl.Token).ConfigureAwait(false))
            {
                try { await VisitAsync(path).ConfigureAwait(false); }
                catch
                {
                    Interlocked.Exchange(ref failed, 1);
                    channel.Writer.TryComplete();
                    crawl.Cancel();
                    throw;
                }
                finally
                {
                    var left = 0;
                    lock (gate) left = --inflight;
                    if (left == 0 || Volatile.Read(ref failed) == 1) channel.Writer.TryComplete();
                }
            }
        }
        Admit(root);
        try
        {
            await Task.WhenAll(WorkerAsync(), WorkerAsync()).ConfigureAwait(false);
            if (Volatile.Read(ref failed) != 0) throw new IOException("网盘索引中断，已写入的曲目会在下次完整同步后校正。");
            RemoteIndexTrack[]? tail = null;
            lock (gate) { if (batch.Count > 0) { tail = batch.ToArray(); batch.Clear(); } }
            if (tail is not null) await CommitAsync(tail).ConfigureAwait(false);
        }
        catch
        {
            crawl.Cancel();
            throw;
        }
        if (source.Provider == "subsonic" && discovered == 0) return new(inserted, updated, unchanged, 0);
        var missing = await index.RetireUnseenAsync(source.Id, syncToken, token).ConfigureAwait(false);
        return new(inserted, updated, unchanged, missing);
    }
    private static bool Inside(string root, string path)
    {
        var normalized = root.TrimEnd('/');
        if (normalized.Length == 0) return path.StartsWith('/');
        return path.TrimEnd('/') == normalized || path.StartsWith(normalized + "/", StringComparison.Ordinal);
    }
    private static RemoteIndexTrack ToTrack(string sourceId, RemoteEntry entry)
    {
        var title = string.IsNullOrWhiteSpace(entry.Title) ? Path.GetFileNameWithoutExtension(entry.Name) : entry.Title;
        var modified = DateTimeOffset.TryParse(entry.Modified, out var date) ? date.UtcDateTime.Ticks : 0;
        var codec = string.IsNullOrWhiteSpace(entry.Codec) ? Path.GetExtension(entry.Name).TrimStart('.').ToUpperInvariant() : entry.Codec.ToUpperInvariant();
        if (codec.Length == 0) codec = "AUDIO";
        var remotePath = entry.Path.StartsWith('/') ? entry.Path : "/" + entry.Path;
        return new(sourceId, remotePath, title, entry.Artist ?? "", entry.Album ?? "",
            entry.SizeBytes ?? 0, modified, entry.DurationSeconds ?? 0, codec);
    }
}
