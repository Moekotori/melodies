using System.Text.Json.Nodes;

namespace Echo.Desktop.Features.Dsp;

internal static class DspParameterPath
{
    public static JsonNode? Read(JsonObject state, string key)
    {
        JsonNode? value = state;
        foreach (var part in key.Split('.'))
            value = value is JsonArray array && int.TryParse(part, out var index)
                ? array[index] : value?[part];
        return value;
    }

    public static void Write(JsonObject state, string key, double value)
        => WriteValue(state, key, JsonValue.Create(value));
    public static void Write(JsonObject state, string key, bool value)
        => WriteValue(state, key, JsonValue.Create(value));
    private static void WriteValue(JsonObject state, string key, JsonValue value)
    {
        var position = key.LastIndexOf('.');
        if (position < 0) { state[key] = value; return; }
        var parent = Read(state, key[..position]) ?? throw new IOException("原生 DSP 参数结构不完整。");
        parent[key[(position + 1)..]] = value;
    }
}
