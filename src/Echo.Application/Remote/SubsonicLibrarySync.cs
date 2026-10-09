using Echo.Contracts.Remote;

namespace Echo.Application.Remote;

// Album pages are written as they arrive. A partial album does not retire songs that were not listed.
internal static class SubsonicLibrarySync
{
    public static async Task<RemoteSyncResult> RunAsync(string sourceId, IAsyncEnumerable<RemoteLibraryPage> pages, IRemoteTrackIndex index,
        IProgress<RemoteSyncProgress>? progress, CancellationToken token)
    {
        var syncToken = Guid.NewGuid().ToString("N");
        var batch = new List<RemoteIndexTrack>(64);
        var inserted = 0; var updated = 0; var unchanged = 0; var discovered = 0; var partial = false;
        async Task FlushAsync()
        {
            if (batch.Count == 0) return;
            var wrote = await index.WriteAsync(batch.ToArray(), syncToken, token).ConfigureAwait(false);
            inserted += wrote.Inserted; updated += wrote.Updated; unchanged += wrote.Unchanged; batch.Clear();
            progress?.Report(new(0, discovered, inserted, updated, unchanged));
        }
        await foreach (var page in pages.ConfigureAwait(false))
        {
            token.ThrowIfCancellationRequested();
            if (page.Partial) partial = true;
            foreach (var track in page.Tracks)
            {
                token.ThrowIfCancellationRequested();
                if (++discovered > RemoteLibrarySync.MaxTracks) throw new IOException("曲目超过 100,000 首，请缩小音乐目录后再索引。");
                batch.Add(track);
                if (batch.Count == 64) await FlushAsync().ConfigureAwait(false);
            }
        }
        await FlushAsync().ConfigureAwait(false);
        if (partial || discovered == 0) return new(inserted, updated, unchanged, 0);
        var missing = await index.RetireUnseenAsync(sourceId, syncToken, token).ConfigureAwait(false);
        return new(inserted, updated, unchanged, missing);
    }
}
