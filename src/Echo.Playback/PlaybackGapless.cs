using System.Text.Json.Nodes;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private bool albumGapless;

    public Task SetAlbumGaplessEnabledAsync(bool enabled, CancellationToken token = default) => Command(async () =>
    {
        albumGapless = enabled;
        if (enabled && snapshot.OperationId > 0 && snapshot.Current is { } track
            && snapshot.Status is PlaybackStatus.Playing or PlaybackStatus.Paused or PlaybackStatus.Loading)
            await TryPrepareAlbumGaplessAsync(track).ConfigureAwait(false);
    }, token, affectsPlayback: false);

    private async Task TryPrepareAlbumGaplessAsync(QueueTrack track)
    {
        if (!albumGapless || automixEnabled || Math.Abs(snapshot.PlaybackRate - 1) > 1e-6 || OutputFormat() != "pcm" || StageActive("sdm")) return;
        var strictRate = StageActive("echoSrc") || deviceConfiguration?.Text("outputMode") is "exclusive" or "asio" or "ks";
        var currentRate = KnownSourceRate(track);
        if (strictRate && currentRate is null) return;
        var prepared = await PrepareNativeQueueAsync(ProjectNativeQueue(queue, track.ItemId), track.ItemId, repeat).ConfigureAwait(false);
        if (prepared.Repeat == "one" || !PlainLocal(track)) return;
        var index = Array.FindIndex(prepared.Items, x => x.ItemId == track.ItemId);
        if (index < 0) return;
        var chain = new List<QueueTrack>(31);
        foreach (var next in GaplessSuccessors(prepared.Items, index, prepared.Repeat == "all"))
        {
            var nextRate = KnownSourceRate(next);
            if (chain.Count == 31 || !PlainLocal(next) || IsDsdPath(next.FilePath) || strictRate && nextRate != currentRate) break;
            chain.Add(next);
        }
        if (chain.Count == 0) return;
        try
        {
            await native.CallAsync("audio.gaplessPrepare", Params(new JsonArray(GaplessRequest(chain)))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* The host keeps ordinary playback when the next track cannot be primed. */ }
    }

    private string OutputFormat() => deviceConfiguration?.Child("processing") is { } processing
        ? processing.Text("outputFormat") ?? "pcm" : "pcm";

    private bool StageActive(string name) => snapshot.Processing?.Child(name) is { } stage && stage.Flag("active");

    private static JsonObject GaplessRequest(List<QueueTrack> chain)
    {
        var request = TrackRequest(chain[0], 1);
        if (chain.Count == 1) return request;
        var following = new JsonArray();
        for (var i = 1; i < chain.Count; i++) following.Add((JsonNode)TrackRequest(chain[i], i + 1));
        request["following"] = following;
        return request;
    }

    private static JsonObject TrackRequest(QueueTrack track, int replayGainIndex)
    {
        var node = new JsonObject
        {
            ["filePath"] = track.FilePath, ["itemId"] = track.ItemId, ["trackId"] = track.TrackId,
            ["replayGainIndex"] = replayGainIndex
        };
        if (track.Metadata is not { Count: > 0 }) return node;
        var metadata = new JsonObject();
        foreach (var pair in track.Metadata)
            if (pair.Value is not null) metadata[pair.Key] = pair.Value;
        if (metadata.Count > 0) node["metadata"] = metadata;
        return node;
    }

    private static IEnumerable<QueueTrack> GaplessSuccessors(IReadOnlyList<QueueTrack> deck, int index, bool repeatAll)
    {
        for (var i = index + 1; i < deck.Count; i++) yield return deck[i];
        if (!repeatAll) yield break;
        for (var i = 0; i < index; i++) yield return deck[i];
        if (deck.Count == 1) yield return deck[0];
    }

    private static bool PlainLocal(QueueTrack track)
    {
        if (track.Metadata?.GetValueOrDefault("isLiveStream") == "true") return false;
        if (track.Source is { } source)
            return source.Kind == "local" && source.Range is null && source.Headers is not { Count: > 0 }
                && string.Equals(source.Uri, track.FilePath, StringComparison.Ordinal);
        return !track.FilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !track.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDsdPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dsf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".dff", StringComparison.OrdinalIgnoreCase);
    }
}
