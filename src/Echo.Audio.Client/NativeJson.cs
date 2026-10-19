using System.Text.Json;

namespace Echo.Playback.NativeAudio;

internal static class NativeJson
{
    public static JsonElement Params(System.Text.Json.Nodes.JsonNode node)
    { using var document = JsonDocument.Parse(node.ToJsonString()); return document.RootElement.Clone(); }
    public static long Long(this JsonElement e, string name, long fallback = 0)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : fallback;
    public static double Number(this JsonElement e, string name, double fallback = 0)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : fallback;
    public static string? Text(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public static bool Flag(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    public static JsonElement? Child(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.Clone() : null;
    public static bool ContainsAny(this string s, char a, char b) => s.IndexOfAny([a, b]) >= 0;
}
