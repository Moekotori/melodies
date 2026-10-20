using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback;

// The native host must acknowledge both plan identity and the selected Alpha DSP.
internal static class AutomixNativePreparation
{
    internal static async Task<bool> PrepareAsync(INativeAudio native, AutomixPlan plan, string nextPath)
    {
        var request = new JsonObject
        {
            ["plan"] = plan.ToJson(),
            ["nextSource"] = new JsonObject { ["kind"] = "local", ["uri"] = nextPath },
        };
        var result = await native.CallAsync("automix.prepare", Parameters(request)).ConfigureAwait(false);
        if (!result.Flag("acknowledged") || result.Text("planId") != plan.PlanId)
            throw new IOException("智能过渡计划回执不匹配。");
        if (plan.AlphaDsp is not null && !result.Flag("alphaDspAcknowledged"))
        {
            // An older host must not silently play its original curve for an Alpha plan.
            await CancelAsync(native, plan.PlanId).ConfigureAwait(false);
            throw new IOException("音频宿主不支持智能过渡 Alpha，本次使用普通衔接。请更新音频宿主。");
        }
        return result.Text("state") == "armed";
    }

    internal static async Task CancelAsync(INativeAudio native, string planId)
    {
        var result = await native.CallAsync("automix.cancel", Parameters(new JsonObject { ["planId"] = planId })).ConfigureAwait(false);
        if (!result.Flag("acknowledged") || result.Text("planId") != planId)
            throw new IOException("音频宿主未确认取消智能过渡，请停止播放后重试。");
    }

    private static JsonElement Parameters(JsonObject request) => PlaybackSessionCoordinator.Params(new JsonArray(request));
}
