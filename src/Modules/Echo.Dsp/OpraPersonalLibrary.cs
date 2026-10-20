using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Core.Contracts;

namespace Echo.Dsp;

public sealed partial class DspResourceService
{
    private async Task<JsonObject> BrowseOpraAsync(DspResourceRequest request, CancellationToken token)
    {
        JsonArray? saved = null;
        var kind = request.Name ?? "vendors";
        if (kind is "favorites" or "recent")
        {
            await writer.WaitAsync(token).ConfigureAwait(false);
            try { saved = Load()[kind == "favorites" ? "opraFavorites" : "opraRecent"]?.DeepClone() as JsonArray ?? new(); }
            finally { writer.Release(); }
        }
        return await opra.BrowseAsync(kind, request.Id, request.Query ?? "", request.Offset, request.Refresh, saved, token).ConfigureAwait(false);
    }

    private JsonObject SaveOpraProduct(DspResourceRequest request, bool recent)
    {
        var id = RequireId(request.Id);
        var key = recent ? "opraRecent" : "opraFavorites";
        var root = Load(); var items = root[key] as JsonArray ?? new();
        var enabled = recent || request.State is { ValueKind: JsonValueKind.Object } state
            && state.TryGetProperty("enabled", out var flag) && flag.ValueKind == JsonValueKind.True;
        var old = items.FirstOrDefault(x => x?.ToString() == id);
        if (old is not null) items.Remove(old);
        if (enabled)
        {
            if (!recent && items.Count >= 100) throw new IOException("最多收藏 100 款耳机，请先取消不用的收藏。");
            items.Insert(0, JsonValue.Create(id));
            while (items.Count > 100) items.RemoveAt(items.Count - 1);
        }
        if (root[key] is null) root[key] = items;
        Write(root);
        return new JsonObject { ["enabled"] = enabled };
    }
}
