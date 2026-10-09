using Echo.Contracts.Remote;
using Echo.Playback;

namespace Echo.Application.Remote;

// The queue keeps remote:// until the moment of playback, so checkpoints never store credentials.
public sealed class RemotePlaybackResolver(RemoteSourceCatalog catalog, IRemoteFileClient files) : IRemoteAudioResolver
{
    public async Task<AudioSource> ResolveAsync(AudioSource remote, CancellationToken token)
    {
        if (!RemoteTrackPaths.TrySplit(remote.Uri, out var sourceId, out var path)) throw new IOException("网盘曲目地址无效。");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(20));
        var source = catalog.GetConnection(sourceId);
        var name = path.TrimEnd('/').Split('/').Last();
        var media = await files.ResolveMediaAsync(source, new RemoteEntry(path, name, false, true, null, null), budget.Token).ConfigureAwait(false);
        return new AudioSource(media.Kind, media.Uri, Headers: media.Headers);
    }
}
