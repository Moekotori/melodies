using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Echo.Dsp;

internal sealed class OpraIndex(string path) : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly SemaphoreSlim lane = new(1, 1);
    private const string Source = "https://opra.roonlabs.net/database_v1.jsonl";
    public async Task<JsonObject> SearchAsync(string query, int offset, bool refresh, CancellationToken token)
    {
        query = query.Trim(); if (query.Length is < 2 or > 100) throw new IOException("请输入 2–100 个字符的品牌或耳机型号。");
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await EnsureIndexAsync(refresh, token).ConfigureAwait(false);
            using var db = Open(path); using var command = db.CreateCommand();
            command.CommandText = "SELECT e.id,p.name,coalesce(v.name,''),e.name,e.parent,coalesce(json_extract(e.data,'$.details'),'') FROM records e JOIN records p ON e.parent=p.id LEFT JOIN records v ON p.parent=v.id WHERE e.kind='eq' AND lower(p.name||' '||coalesce(v.name,'')||' '||e.name) LIKE $query ESCAPE '!' ORDER BY v.name,p.name,e.name,e.id LIMIT 31 OFFSET $offset";
            command.Parameters.AddWithValue("$query", "%" + query.ToLowerInvariant().Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%");
            command.Parameters.AddWithValue("$offset", Math.Clamp(offset, 0, 100000));
            var items = new JsonArray(); using var reader = command.ExecuteReader();
            while (reader.Read()) items.Add((JsonNode)new JsonObject { ["id"] = reader.GetString(0), ["product"] = reader.GetString(1), ["vendor"] = reader.GetString(2), ["author"] = reader.GetString(3), ["productId"] = reader.GetString(4), ["details"] = reader.GetString(5) });
            var more = items.Count > 30; if (more) items.RemoveAt(30);
            return new JsonObject { ["items"] = items, ["hasMore"] = more, ["source"] = Source, ["license"] = "CC-BY-SA-4.0" };
        }
        finally { lane.Release(); }
    }
    public async Task<JsonObject> PresetAsync(string id, CancellationToken token)
    {
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await EnsureIndexAsync(false, token).ConfigureAwait(false);
            using var db = Open(path); using var command = db.CreateCommand();
            command.CommandText = "SELECT e.data,p.name,coalesce(v.name,'') FROM records e JOIN records p ON e.parent=p.id LEFT JOIN records v ON p.parent=v.id WHERE e.kind='eq' AND e.id=$id";
            command.Parameters.AddWithValue("$id", id); using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new IOException("OPRA 预设不存在，请重新搜索。");
            return OpraPresetMapping.Create(id, reader.GetString(2) + " / " + reader.GetString(1), JsonNode.Parse(reader.GetString(0))!.AsObject());
        }
        finally { lane.Release(); }
    }
    private async Task EnsureIndexAsync(bool refresh, CancellationToken token)
    {
        if (!refresh && File.Exists(path)) return;
        var temporary = path + ".download"; if (File.Exists(temporary)) File.Delete(temporary);
        try
        {
            using var response = await Http.GetAsync(Source, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 64 * 1024 * 1024) throw new IOException("OPRA 下载超过 64 MiB 预算。");
            using var db = Open(temporary); using var setup = db.CreateCommand();
            setup.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA cache_size=-1024; CREATE TABLE records(id TEXT PRIMARY KEY,kind TEXT NOT NULL,parent TEXT NOT NULL,name TEXT NOT NULL,data TEXT NOT NULL); CREATE INDEX records_parent ON records(parent);"; setup.ExecuteNonQuery();
            using (var transaction = db.BeginTransaction())
            using (var insert = db.CreateCommand())
            {
                insert.Transaction = transaction; insert.CommandText = "INSERT OR REPLACE INTO records VALUES($id,$kind,$parent,$name,$data)";
                foreach (var key in new[] { "$id", "$kind", "$parent", "$name", "$data" }) insert.Parameters.Add(key, SqliteType.Text);
                await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false); var count = 0;
                await foreach (var line in LinesAsync(stream, token).ConfigureAwait(false))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var record = JsonNode.Parse(line) as JsonObject; var kind = record?["type"]?.ToString(); var id = record?["id"]?.ToString(); var data = record?["data"] as JsonObject;
                    if (id is null || id.Length > 128 || data is null || kind is not ("eq" or "vendor" or "product")) continue;
                    if (kind == "eq" && data["type"]?.ToString() != "parametric_eq") continue;
                    if (++count > 100000) throw new IOException("OPRA 条目超过索引预算。");
                    insert.Parameters["$id"].Value = id; insert.Parameters["$kind"].Value = kind;
                    insert.Parameters["$parent"].Value = data[kind == "eq" ? "product_id" : "vendor_id"]?.ToString() ?? "";
                    insert.Parameters["$name"].Value = data[kind == "eq" ? "author" : "name"]?.ToString() ?? "OPRA";
                    insert.Parameters["$data"].Value = data.ToJsonString(); insert.ExecuteNonQuery();
                }
                if (count < 1) throw new IOException("OPRA 没有返回可用条目。"); transaction.Commit();
            }
            db.Close(); File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static SqliteConnection Open(string file)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()); db.Open(); return db;
    }
    private static async IAsyncEnumerable<string> LinesAsync(Stream stream, [EnumeratorCancellation] CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 8192); var buffer = new char[8192]; var line = new StringBuilder(1024); long total = 0; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            total += count; if (total > 32 * 1024 * 1024) throw new IOException("OPRA 文本超过预算。");
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] == '\n') { yield return line.ToString().TrimEnd('\r'); line.Clear(); }
                else { if (line.Length >= 128 * 1024) throw new IOException("OPRA 单条记录过大。"); line.Append(buffer[i]); }
            }
        }
        if (line.Length > 0) yield return line.ToString();
    }
    public void Dispose() => lane.Dispose();
}
