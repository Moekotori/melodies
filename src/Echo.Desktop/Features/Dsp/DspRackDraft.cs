using System.Text.Json.Nodes;

namespace Echo.Desktop.Features.Dsp;

internal static class DspRackDraft
{
    public static void Reorder(JsonObject state, IReadOnlyList<string> order)
    {
        if (order.Count is < 1 or > 24 || order.Distinct(StringComparer.Ordinal).Count() != order.Count)
            throw new IOException("无效的 DSP 机架顺序。");
        var baseIds = state["order"]!.AsArray().Select(node => node!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var filters = state["filters"] as JsonArray ?? new JsonArray();
        var all = baseIds.Concat(filters.Select(node => node!["id"]!.GetValue<string>())).ToHashSet(StringComparer.Ordinal);
        if (order.Count != all.Count || !all.SetEquals(order)) throw new IOException("DSP 处理链节点不完整。");
        state["order"] = new JsonArray(order.Where(baseIds.Contains).Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());
        state["sequence"] = new JsonArray(order.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());
    }
}
