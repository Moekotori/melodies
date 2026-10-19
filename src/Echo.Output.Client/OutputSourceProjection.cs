using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Output.Client;

internal sealed class OutputSourceProjection(MediaTicketRegistry tickets)
{
    private readonly Dictionary<string, OutputMediaTicket> pending = new(StringComparer.Ordinal);
    public string BaseAddress { get; set; } = "";
    public OutputMediaTicket[] TakeTickets() { var result = pending.Values.ToArray(); pending.Clear(); return result; }
    private string Url(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new NotSupportedException("网络 Output V1 仅支持 Core 本地的完整音频文件。");
        var ticket = tickets.Issue(path); pending[ticket.Id] = ticket;
        return BaseAddress + ticket.Id + "/audio" + ticket.Extension;
    }
    private JsonObject Source(JsonObject source)
    {
        if (source["kind"]?.GetValue<string>() != "local" || source["range"] is not null || source["mix"] is not null)
            throw new NotSupportedException("网络 Output V1 不支持 CUE 范围、混音源或网盘/直播音源。");
        return new() { ["kind"] = "http", ["uri"] = Url(source["uri"]!.GetValue<string>()) };
    }
    public (string Method, JsonElement Parameters) Rewrite(string method, JsonElement parameters)
    {
        pending.Clear();
        var node = JsonNode.Parse(parameters.GetRawText());
        JsonObject Argument() => node is JsonArray array ? array[0]!.AsObject() : node!.AsObject();
        switch (method)
        {
            case "queue.set":
                var queue = node!.AsObject(); var items = queue["items"]!.AsArray();
                if (items.Count > 1001) throw new ArgumentException("Output 队列过大。");
                foreach (var entry in items)
                {
                    var track = entry!.AsObject();
                    var source = track["source"] is JsonObject descriptor ? Source(descriptor)
                        : new JsonObject { ["kind"] = "http", ["uri"] = Url(track["filePath"]!.GetValue<string>()) };
                    track["filePath"] = source["uri"]!.GetValue<string>(); track["source"] = source;
                }
                break;
            case "audio.openFile":
                var open = Argument(); open["source"] = new JsonObject { ["kind"] = "http", ["uri"] = Url(open["filePath"]!.GetValue<string>()) };
                open.Remove("filePath"); method = "audio.openSource"; break;
            case "audio.openSource": Argument()["source"] = Source(Argument()["source"]!.AsObject()); break;
            case "audio.gaplessPrepare":
                var gapless = Argument(); gapless["filePath"] = Url(gapless["filePath"]!.GetValue<string>());
                if (gapless["following"] is JsonArray following)
                    foreach (var item in following) item!["filePath"] = Url(item["filePath"]!.GetValue<string>());
                break;
            case "audio.prefetch": Argument()["filePath"] = Url(Argument()["filePath"]!.GetValue<string>()); break;
        }
        return (method, JsonSerializer.SerializeToElement(node, OutputNodeJson.Default.JsonNode));
    }
    public JsonElement RestorePaths(JsonElement value)
    {
        if (BaseAddress.Length == 0 || !ContainsDelegate(value)) return value;
        var node = JsonNode.Parse(value.GetRawText()); Visit(node);
        return JsonSerializer.SerializeToElement(node, OutputNodeJson.Default.JsonNode);
    }
    private bool ContainsDelegate(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString()?.Contains(BaseAddress, StringComparison.Ordinal) == true;
        if (value.ValueKind == JsonValueKind.Object)
        { foreach (var property in value.EnumerateObject()) if (ContainsDelegate(property.Value)) return true; }
        else if (value.ValueKind == JsonValueKind.Array)
        { foreach (var item in value.EnumerateArray()) if (ContainsDelegate(item)) return true; }
        return false;
    }
    private void Visit(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
                if (Redact(array[index]) is { } replacement) array[index] = replacement; else Visit(array[index]);
            return;
        }
        if (node is not JsonObject obj) return;
        foreach (var pair in obj.ToArray())
            if (Redact(pair.Value) is { } replacement) obj[pair.Key] = replacement; else Visit(pair.Value);
    }
    private JsonNode? Redact(JsonNode? value)
    {
        if (BaseAddress.Length == 0 || value is not JsonValue text || !text.TryGetValue<string>(out var path) || !path.Contains(BaseAddress, StringComparison.Ordinal)) return null;
        if (!path.StartsWith(BaseAddress, StringComparison.Ordinal)) return JsonValue.Create("Output 媒体操作失败，请检查音源或设备。");
        var id = path[BaseAddress.Length..].Split('/')[0];
        return JsonValue.Create(tickets.PathFor(id) ?? "output://media-unavailable");
    }
}
[System.Text.Json.Serialization.JsonSerializable(typeof(JsonNode))]
internal partial class OutputNodeJson : System.Text.Json.Serialization.JsonSerializerContext;
