using System.Text.Json.Nodes;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private bool albumGapless;
    private bool gaplessContinuation;
    private string[] gaplessChain = [];

    public Task SetAlbumGaplessEnabledAsync(bool enabled, CancellationToken token = default) => Command(async () =>
    {
        if (albumGapless == enabled) return;
        var prior = albumGapless;
        albumGapless = enabled;
        try
        {
            if (initialized && snapshot.OperationId > 0)
                await SendQueueAsync(queue, snapshot.Current?.ItemId, repeat).ConfigureAwait(false);
        }
        catch { albumGapless = prior; throw; }
        await RefreshAlbumGaplessAsync().ConfigureAwait(false);
    }, token, affectsPlayback: false);

    private bool CanPrepareGapless() => albumGapless && !automixEnabled && !shuffle && segmentLoop is null
        && repeat != RepeatMode.One && Math.Abs(snapshot.PlaybackRate - 1) <= 1e-6
        && OutputFormat() == "pcm" && !StageActive("sdm");

    private async Task RefreshAlbumGaplessAsync()
    {
        if (snapshot.OperationId <= 0 || snapshot.Current is not { } track
            || snapshot.Status is not (PlaybackStatus.Playing or PlaybackStatus.Paused or PlaybackStatus.Loading)) return;
        if (gaplessContinuation)
        {
            // Once audible, the continuation FIFO contains the current song too. Never cancel
            // or replace that FIFO underneath playback: reopen at the confirmed position instead.
            var at = Array.IndexOf(gaplessChain, track.ItemId);
            var projected = ProjectNativeQueue(queue, track.ItemId);
            var current = Array.FindIndex(projected, x => x.ItemId == track.ItemId);
            if (CanPrepareGapless() && at >= 0 && current >= 0
                && GaplessSuccessors(projected, current, repeat == RepeatMode.All)
                    .Take(gaplessChain.Length - at - 1).Select(x => x.ItemId).SequenceEqual(gaplessChain.Skip(at + 1))) return;
            await OpenAsync(track, snapshot.PositionSeconds, snapshot.Status == PlaybackStatus.Paused).ConfigureAwait(false);
            return;
        }
        await TryPrepareAlbumGaplessAsync(track).ConfigureAwait(false);
    }

    private async Task TryPrepareAlbumGaplessAsync(QueueTrack track)
    {
        if (!CanPrepareGapless() || gaplessContinuation || IsDsdPath(track.FilePath)) return;
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
            var receipt = await native.CallAsync("audio.gaplessPrepare", Params(new JsonArray(GaplessRequest(chain)))).ConfigureAwait(false);
            gaplessChain = receipt.Flag("prepared") ? chain.Select(x => x.ItemId).ToArray() : [];
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
