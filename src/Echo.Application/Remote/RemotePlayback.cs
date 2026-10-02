using System.Security.Cryptography;
using System.Text;
using Echo.Contracts.Remote;
using Echo.Playback;

namespace Echo.Application.Remote;

public static class RemotePlayback
{
    public static async Task<QueueTrack> CreateTrackAsync(RemoteSourceCatalog catalog, IRemoteFileClient client, string sourceId, RemoteEntry entry, CancellationToken token)
    {
        if (entry.IsDirectory || !entry.IsAudio) throw new ArgumentException("请选择音频文件。");
        var source = catalog.GetConnection(sourceId);
        var media = await client.ResolveMediaAsync(source, entry, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var id = "remote:" + sourceId + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.MediaId ?? entry.Path)))[..24];
        // History keeps only a safe identity for HTTP media; signed/API-key URLs stay in the transient source.
        var filePath = media.Kind == "http" ? "echo-remote:" + id : media.Uri;
        return new(id + ":" + Guid.NewGuid().ToString("N"), id, filePath, new(media.Kind, media.Uri, Headers: media.Headers),
            new Dictionary<string, string?> { ["title"] = entry.Title ?? Path.GetFileNameWithoutExtension(entry.Name), ["artist"] = entry.Artist ?? source.Name,
                ["album"] = entry.Album, ["duration"] = entry.DurationSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["codec"] = entry.Codec ?? Path.GetExtension(entry.Name).TrimStart('.').ToUpperInvariant(), ["remoteSourceId"] = sourceId }, Origin: new("remote", source.Name, sourceId));
    }
}
