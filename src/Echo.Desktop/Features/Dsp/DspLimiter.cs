using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    private async Task<JsonElement> PushLimiterAsync(JsonObject state, bool retain, CancellationToken token)
    {
        if (session is null) throw new IOException("播放服务尚未连接。");
        var ceiling = Bool(state, "limiterAutoCeiling") ? null : state["dspLimiterCeilingDb"]?.DeepClone();
        await session.DspAsync("dsp.setLimiterCeiling", Json(new JsonArray(new JsonNode?[] { ceiling })), retain, token);
        await session.DspAsync("dsp.setLimiterRelease", Json(new JsonArray(state["dspLimiterReleaseMs"]?.DeepClone() ?? JsonValue.Create(80))), retain, token);
        return await session.DspAsync("dsp.setSafetyLimiter", Json(new JsonArray(state["safetyLimiterEnabled"]?.DeepClone() ?? JsonValue.Create(false))), retain, token);
    }
}
