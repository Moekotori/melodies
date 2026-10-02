using System.Globalization;
using System.Text.Json;

namespace Echo.Desktop.Features.Player;

internal static class SignalPathFacts
{
    public static JsonElement? Child(JsonElement? json, string name) => json is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Object ? item : null;
    public static string? Text(JsonElement? json, string name) => json is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    public static double? Number(JsonElement? json, string name) => json is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number
        && item.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
    public static bool Flag(JsonElement? json, string name) => json is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.True;
    public static bool? Boolean(JsonElement? json, string name) => json is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(name, out var item) && item.ValueKind is JsonValueKind.True or JsonValueKind.False ? item.GetBoolean() : null;
    public static string Rate(double? rate) => rate is > 0 ? (rate.Value / 1000).ToString("0.###", CultureInfo.InvariantCulture) + " kHz" : "";
    public static string Join(params string?[] parts) => string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed record SignalPathFact(string Label, string Value, string Detail = "", bool Highlight = false);
public sealed record SignalPathNode(string Icon, string Title, string Value, string Detail);
