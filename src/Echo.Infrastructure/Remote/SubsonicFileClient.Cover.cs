using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

internal sealed partial class SubsonicFileClient
{
    private readonly Dictionary<string, string> coverIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]?> coverBytes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> coverMisses = new(StringComparer.Ordinal);
    private readonly object coverGate = new();
    private long coverBytesUsed;
    public async Task<byte[]?> ReadArtworkAsync(RemoteConnection source, string remotePath, CancellationToken token)
    {
        try
        {
            var songId = RemoteBrowseRules.VirtualId(remotePath).Id;
            var coverId = CoverId(songId);
            if (coverId is null)
            {
                using var json = await http.JsonAsync(Url(source, "getSong", new() { ["id"] = songId }), token).ConfigureAwait(false);
                var song = Property(Response(json.RootElement), "song");
                coverId = RemoteBrowseRules.Text(song, "coverArt", 256);
                if (coverId.Length == 0) coverId = RemoteBrowseRules.Text(song, "albumId", 256);
                RememberCover(songId, coverId);
            }
            if (coverId.Length == 0 || Missed(coverId)) return null;
            var cached = CachedCover(coverId);
            if (cached is not null) return cached;
            var bytes = await http.BytesAsync(Url(source, "getCoverArt", new() { ["id"] = coverId, ["size"] = "512" }, binary: true), 4 * 1024 * 1024, token).ConfigureAwait(false);
            if (bytes.Length <= 32) { MarkMiss(coverId); return null; }
            StoreCover(coverId, bytes);
            return bytes;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }
    private void RememberCover(string songId, string coverId)
    {
        if (songId.Length == 0 || coverId.Length == 0) return;
        lock (coverGate)
        {
            if (coverIds.Count >= 8192 && !coverIds.ContainsKey(songId)) coverIds.Clear();
            coverIds[songId] = coverId;
        }
    }
    private string? CoverId(string songId)
    {
        lock (coverGate) return coverIds.TryGetValue(songId, out var cover) ? cover : null;
    }
    private byte[]? CachedCover(string coverId)
    {
        lock (coverGate) return coverBytes.TryGetValue(coverId, out var bytes) ? bytes : null;
    }
    private bool Missed(string coverId)
    {
        lock (coverGate)
        {
            if (!coverMisses.TryGetValue(coverId, out var expiry)) return false;
            if (expiry > Environment.TickCount64) return true;
            coverMisses.Remove(coverId); return false;
        }
    }
    private void MarkMiss(string coverId)
    {
        lock (coverGate)
        {
            if (coverMisses.Count >= 2048) coverMisses.Clear();
            coverMisses[coverId] = Environment.TickCount64 + 10 * 60 * 1000;
        }
    }
    private void StoreCover(string coverId, byte[] bytes)
    {
        lock (coverGate)
        {
            if (coverBytes.TryGetValue(coverId, out var previous) && previous is not null) coverBytesUsed -= previous.Length;
            coverBytes[coverId] = bytes; coverBytesUsed += bytes.Length;
            while ((coverBytes.Count > 8 || coverBytesUsed > 16 * 1024 * 1024) && coverBytes.Count > 1)
            {
                var oldest = coverBytes.Keys.First();
                if (oldest == coverId) break;
                if (coverBytes.TryGetValue(oldest, out var removed) && removed is not null) coverBytesUsed -= removed.Length;
                coverBytes.Remove(oldest);
            }
        }
    }
}
