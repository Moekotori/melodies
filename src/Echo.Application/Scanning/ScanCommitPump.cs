using System.Threading.Channels;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Application.Scanning;

internal static class ScanCommitPump
{
    public static async Task RunAsync(ILibraryStore store, string job, ChannelReader<ScanEntry> source,
        int batchSize, ScanCounters counters, CancellationToken cancellation)
    {
        var batch = new List<ScanEntry>(batchSize);
        long sequence = 0;
        while (await source.WaitToReadAsync(cancellation).ConfigureAwait(false))
        {
            batch.Clear();
            var characters = 0;
            using var flush = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            flush.CancelAfter(TimeSpan.FromMilliseconds(50));
            while (batch.Count < batchSize && characters < 200 * 1024)
            {
                while (source.TryRead(out var entry))
                {
                    batch.Add(entry);
                    characters += entry.File.Path.Length + entry.File.PathKey.Length +
                        (entry.Metadata is { } m ? m.Title.Length + m.Artist.Length + m.Album.Length + m.AlbumArtist.Length + m.Genre.Length : 0);
                    if (batch.Count == batchSize || characters >= 200 * 1024) break;
                }
                if (batch.Count == batchSize || characters >= 200 * 1024) break;
                try { if (!await source.WaitToReadAsync(flush.Token).ConfigureAwait(false)) break; }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { break; }
            }
            cancellation.ThrowIfCancellationRequested();
            if (batch.Count == 0) continue;
            var receipt = await store.CommitScanBatchAsync(job, ++sequence, batch, cancellation).ConfigureAwait(false);
            counters.Inserted += receipt.Inserted; counters.Updated += receipt.Updated;
            counters.Unchanged += receipt.Unchanged; counters.Failed += receipt.Failed;
            counters.Publish("committing");
        }
    }
}
