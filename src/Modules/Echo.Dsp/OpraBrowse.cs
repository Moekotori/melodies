using System.Text.Json.Nodes;

namespace Echo.Dsp;

internal sealed partial class OpraIndex
{
    public async Task<JsonObject> BrowseAsync(string kind, string? parent, string query, int offset,
        bool refresh, JsonArray? saved, CancellationToken token)
    {
        if (kind is not ("vendors" or "products" or "presets" or "favorites" or "recent")) throw new IOException("无效的 OPRA 浏览分类。");
        if (query.Length > 100 || parent?.Length > 128 || offset is < 0 or > 100000) throw new IOException("OPRA 查询超出范围。");
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // Opening a page never starts a download. Refresh is an explicit action.
            if (!refresh && !File.Exists(path)) return new JsonObject { ["items"] = new JsonArray(), ["hasMore"] = false, ["cached"] = false };
            await EnsureIndexAsync(refresh, token).ConfigureAwait(false);
            using var db = Open(path); using var command = db.CreateCommand();
            command.Parameters.AddWithValue("$parent", parent ?? "");
            command.Parameters.AddWithValue("$offset", offset);
            command.Parameters.AddWithValue("$saved", saved?.ToJsonString() ?? "[]");
            var terms = query.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(16).ToArray();
            var search = "";
            for (var i = 0; i < terms.Length; i++)
            {
                search += $"AND (p.name||' '||v.name) LIKE $term{i} ESCAPE '!' ";
                command.Parameters.AddWithValue("$term" + i, "%" + terms[i].Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%");
            }
            const string products = " FROM records p JOIN records v ON p.parent=v.id JOIN records e ON e.parent=p.id AND e.kind='eq' ";
            command.CommandText = kind switch
            {
                "vendors" => "SELECT v.id,v.name,'',count(DISTINCT p.id),count(e.id)" + products
                    + "WHERE p.kind='product' GROUP BY v.id ORDER BY v.name COLLATE NOCASE,v.id LIMIT 31 OFFSET $offset",
                "presets" => "SELECT e.id,e.name,coalesce(json_extract(e.data,'$.details'),''),0,0 FROM records e WHERE e.kind='eq' AND e.parent=$parent ORDER BY e.name,e.id LIMIT 31 OFFSET $offset",
                _ => "SELECT p.id,p.name,v.name,count(e.id),0" + products
                    + "WHERE p.kind='product' AND ($parent='' OR v.id=$parent) " + search
                    + (kind is "favorites" or "recent" ? "AND p.id IN (SELECT value FROM json_each($saved)) " : "")
                    + "GROUP BY p.id ORDER BY " + (kind == "recent" ? "(SELECT key FROM json_each($saved) WHERE value=p.id)," : "")
                    + "v.name COLLATE NOCASE,p.name COLLATE NOCASE,p.id LIMIT 31 OFFSET $offset"
            };
            var items = new JsonArray(); using (var reader = command.ExecuteReader())
                while (reader.Read()) items.Add((JsonNode)new JsonObject
                {
                    ["id"] = reader.GetString(0), ["name"] = reader.GetString(1), ["detail"] = reader.GetString(2),
                    ["count"] = reader.GetInt32(3), ["curves"] = reader.GetInt32(4)
                });
            var more = items.Count > 30; if (more) items.RemoveAt(30);
            return new JsonObject { ["items"] = items, ["hasMore"] = more, ["cached"] = true };
        }
        finally { lane.Release(); }
    }
}
