using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback;

internal static class NativeDspRackCompatibility
{
    private static readonly HashSet<string> Legacy = new(StringComparer.Ordinal)
    { "equalizer", "convolution", "replayGain", "compressor", "crossfeed", "stereoField", "channelMatrix", "channelBalance" };

    internal static bool NeedsUpgrade(JsonElement request)
    {
        var state = request.ValueKind == JsonValueKind.Array && request.GetArrayLength() > 0 ? request[0] : request;
        if (state.ValueKind != JsonValueKind.Object || !state.TryGetProperty("order", out var order)
            || order.ValueKind != JsonValueKind.Array || order.GetArrayLength() != Legacy.Count) return false;
        return order.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String && Legacy.Contains(x.GetString()!))
            && order.EnumerateArray().Select(x => x.GetString()).Distinct(StringComparer.Ordinal).Count() == Legacy.Count;
    }

    internal static JsonElement Upgrade(JsonElement request, JsonElement nativeState)
    {
        var root = JsonNode.Parse(request.GetRawText())!;
        var state = root is JsonArray array ? array[0]!.AsObject() : root.AsObject();
        var order = state["order"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        var defaults = nativeState.GetProperty("order").EnumerateArray().Select(x => x.GetString()!).ToArray();
        if (defaults.Length > 24) throw new IOException("原生 DSP 机架超出恢复上限");
        for (var i = 0; i < defaults.Length; i++)
        {
            if (order.Contains(defaults[i], StringComparer.Ordinal)) continue;
            var preceding = i > 0 ? order.IndexOf(defaults[i - 1]) : -1;
            order.Insert(preceding + 1, defaults[i]);
        }
        state["order"] = new JsonArray(order.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
        using var json = JsonDocument.Parse(root.ToJsonString());
        return json.RootElement.Clone();
    }
}

public sealed partial class PlaybackSessionCoordinator
{
    private async Task<JsonElement> AdaptDspRequestAsync(string method, JsonElement request)
    {
        if (method != "dspRack.setState" || !NativeDspRackCompatibility.NeedsUpgrade(request)) return request;
        var state = await native.CallAsync("dspRack.getState").ConfigureAwait(false);
        return NativeDspRackCompatibility.Upgrade(request, state);
    }
}
