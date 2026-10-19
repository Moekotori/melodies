using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    public async Task SetPlaybackRateAsync(double rate, CancellationToken token = default)
    {
        if (!double.IsFinite(rate) || rate is < .5 or > 2) throw new ArgumentOutOfRangeException(nameof(rate));
        await DspAsync("playbackRate.configure", Params(new JsonArray(new JsonObject
        { ["rate"] = rate, ["mode"] = PlaybackRateMode })), retainForRecovery: true, token: token).ConfigureAwait(false);
    }

    private void ConfirmPlaybackRate(JsonElement receipt)
    {
        var rate = receipt.Number("rate", double.NaN);
        if (!double.IsFinite(rate) || rate is < .5 or > 2 || receipt.Text("mode") is not ("speed" or "nightcore" or "daycore"))
            throw new IOException("原生播放速度未确认");
        Publish(snapshot with { PlaybackRate = rate, Error = null });
        Volatile.Write(ref rateMode, receipt.Text("mode")!);
    }

    private double RestoredPlaybackRate()
    {
        if (!dspCommands.TryGetValue("playbackRate.configure:-1", out var request)) return 1;
        var config = request.ValueKind == JsonValueKind.Array && request.GetArrayLength() > 0 ? request[0] : request;
        var rate = config.Number("rate", double.NaN);
        if (!double.IsFinite(rate) || rate is < .5 or > 2) throw new IOException("保存的播放速度无效");
        return rate;
    }

    private async Task ReplayRetainedDspAsync()
    {
        foreach (var entry in dspCommands.ToArray())
        {
            var method = entry.Key[..entry.Key.IndexOf(':')];
            var request = await AdaptDspRequestAsync(method, entry.Value).ConfigureAwait(false);
            var receipt = await native.CallAsync(method, request).ConfigureAwait(false);
            dspCommands[entry.Key] = request;
            if (method == "playbackRate.configure") ConfirmPlaybackRate(receipt);
        }
    }
}
