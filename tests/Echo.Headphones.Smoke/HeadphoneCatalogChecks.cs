using System.Text.Json;
using Echo.Core.Contracts;
using Echo.Dsp;
using Microsoft.Data.Sqlite;

internal static class HeadphoneCatalogChecks
{
    internal static void Run(DspResourceService service, string root)
    {
        var client = new LocalDspResources(service);
        var empty = client.ExecuteAsync(DspResourceOperation.BrowseOpra).GetAwaiter().GetResult();
        Check(!empty.GetProperty("cached").GetBoolean(), "opening an uncached page must not download");
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "opra.sqlite")};Pooling=False"))
        {
            db.Open(); using var setup = db.CreateCommand();
            setup.CommandText = "CREATE TABLE records(id TEXT PRIMARY KEY,kind TEXT,parent TEXT,name TEXT,data TEXT);CREATE INDEX records_parent ON records(parent)"; setup.ExecuteNonQuery();
            void Add(string id, string kind, string parent, string name, string data = "{}")
            {
                using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO records VALUES($id,$kind,$parent,$name,$data)";
                cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$kind", kind); cmd.Parameters.AddWithValue("$parent", parent);
                cmd.Parameters.AddWithValue("$name", name); cmd.Parameters.AddWithValue("$data", data); cmd.ExecuteNonQuery();
            }
            for (var i = 0; i < 34; i++)
            {
                Add("v" + i, "vendor", "", i == 0 ? "Sennheiser" : "Vendor " + i);
                Add("p" + i, "product", "v" + i, i == 0 ? "HD 650" : "Model " + i);
                Add("e" + i, "eq", "p" + i, "oratory1990", "{\"product_id\":\"p" + i + "\",\"author\":\"oratory1990\",\"parameters\":{\"gain_db\":-8.123456789,\"bands\":[{\"type\":\"peak_dip\",\"frequency\":1050.123456,\"gain_db\":6.23456789,\"q\":1.23456789}]}}");
            }
        }
        var first = client.ExecuteAsync(DspResourceOperation.BrowseOpra).Result;
        Check(first.GetProperty("items").GetArrayLength() == 30 && first.GetProperty("hasMore").GetBoolean(), "vendor pagination");
        Check(client.ExecuteAsync(DspResourceOperation.BrowseOpra, offset: 30).Result.GetProperty("items").GetArrayLength() == 4, "vendor next page");
        var search = client.ExecuteAsync(DspResourceOperation.BrowseOpra, name: "products", query: "Sennheiser HD 650").Result;
        Check(search.GetProperty("items").GetArrayLength() == 1, "brand/model token search");
        Check(client.ExecuteAsync(DspResourceOperation.BrowseOpra, name: "products", query: "%").Result.GetProperty("items").GetArrayLength() == 0, "SQL wildcard escaping");
        using var enabled = JsonDocument.Parse("{\"enabled\":true}");
        client.ExecuteAsync(DspResourceOperation.FavoriteOpra, id: "p0", state: enabled.RootElement).GetAwaiter().GetResult();
        client.ExecuteAsync(DspResourceOperation.RecentOpra, id: "p1").GetAwaiter().GetResult();
        client.ExecuteAsync(DspResourceOperation.RecentOpra, id: "p0").GetAwaiter().GetResult();
        using var reopened = new DspResourceService(root);
        var second = new LocalDspResources(reopened);
        Check(second.ExecuteAsync(DspResourceOperation.BrowseOpra, name: "favorites").Result.GetProperty("items")[0].GetProperty("id").GetString() == "p0", "favorite persistence");
        Check(second.ExecuteAsync(DspResourceOperation.BrowseOpra, name: "recent").Result.GetProperty("items")[0].GetProperty("id").GetString() == "p0", "recent order persistence");
        Console.WriteLine("PASS bounded catalog pages, token search, wildcard escaping, Core favorites/recent persistence");
    }
    internal static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}

internal sealed class LocalDspResources(DspResourceService service) : IDspResourceClient
{
    public Task<JsonElement> ExecuteAsync(DspResourceOperation operation, string? id = null, string? name = null, string? path = null,
        string? query = null, int offset = 0, JsonElement? state = null, bool refresh = false, CancellationToken token = default)
        => service.ExecuteAsync(new(CoreDspProtocol.Version, Guid.NewGuid().ToString(), operation, id, name, path, query, offset, state, refresh), token);
}
