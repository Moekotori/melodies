using System.Threading.Channels;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Contracts.Tasks;

namespace Echo.Application.Scanning;

internal static class ScanCommitPump
{
    public static async Task RunAsync(ILibraryStore store, string job, ChannelReader<ScanEntry> source,
        int batchSize, ScanCounters counters, CancellationToken cancellation, IBackgroundWorkAdmission? admission = null)
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
            var offset = 0;
            while (offset < batch.Count)
            {
                if (admission is not null)
                {
                    if (admission.Snapshot.Pressure == PlaybackWorkPressure.Protected) counters.Publish("waiting_playback", true);
                    await admission.WaitAsync(BackgroundWorkKind.Commit, cancellation).ConfigureAwait(false);
                }
                var count = Math.Min(batch.Count - offset, Math.Min(batchSize, admission?.Snapshot.CommitBatchLimit ?? batchSize));
                var slice = offset == 0 && count == batch.Count ? batch : batch.GetRange(offset, count);
                var receipt = await store.CommitScanBatchAsync(job, ++sequence, slice, cancellation).ConfigureAwait(false);
                counters.Inserted += receipt.Inserted; counters.Updated += receipt.Updated;
                counters.Unchanged += receipt.Unchanged; counters.Failed += receipt.Failed;
                offset += count;
                counters.Publish("committing");
            }
        }
    }
}
