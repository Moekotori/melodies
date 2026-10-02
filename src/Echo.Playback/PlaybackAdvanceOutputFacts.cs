using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private JsonElement? OutputFactsAfterAdvance(JsonElement value)
    {
        if (snapshot.Output is not { ValueKind: JsonValueKind.Object } old) return null;
        var actual = value.Long("actualSampleRate");
        if (actual <= 0) return old;
        var updated = JsonNode.Parse(old.GetRawText())!.AsObject();
        var changed = old.Long("hardwareSampleRate", old.Long("sampleRate")) != actual;
        updated["sampleRate"] = actual; updated["hardwareSampleRate"] = actual;
        updated["sharedDeviceSampleRate"] = actual; updated["sharedSampleRate"] = actual;
        if (changed)
            foreach (var name in new[] { "actualBufferFrames", "nativeActualBufferFrames", "deviceBufferFrames", "openedDeviceBufferFrames", "bufferSizeFallback" }) updated.Remove(name);
        foreach (var name in new[] { "actualBufferFrames", "nativeActualBufferFrames", "deviceBufferFrames", "openedDeviceBufferFrames" })
            if (value.Long(name) is > 0 and var frames) updated[name] = frames;
        return Params(updated);
    }
}
