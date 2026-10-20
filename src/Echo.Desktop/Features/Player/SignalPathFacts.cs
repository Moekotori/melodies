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
// Tone is one of good / proc / warn / danger / muted; the template keys its colors off the derived flags.
public sealed record SignalPathNode(string Icon, string Title, string Value, string Detail,
    bool IsLast = false, bool IsProcessing = false, string Tone = "good",
    IReadOnlyList<SignalPathFact>? Facts = null, string Tag = "")
{
    public IReadOnlyList<SignalPathFact> Rows => Facts ?? [];
    public bool HasTag => Tag.Length > 0;
    public bool HasSummary => Rows.Count > 0;
    public bool IsGood => Tone == "good";
    public bool IsProc => Tone == "proc";
    public bool IsWarn => Tone == "warn";
    public bool IsDanger => Tone == "danger";
    public bool IsMuted => Tone == "muted";
}
public sealed record SignalPathChip(string Label, string Value, bool Active);
public sealed record SignalPathInsight(string Title, string Detail, string Advice);
