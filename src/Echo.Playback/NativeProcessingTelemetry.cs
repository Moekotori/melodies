using System.Text.Json;

namespace Echo.Playback;

public sealed record NativeProcessResources(long ProcessId, long WorkingSetBytes, long? PrivateBytes);
public sealed record SdmRealtimePerformance(string State, string Stream, bool IndependentlyCalibrated,
    bool ExecutionMatchesObservation, long? Blocks, long? DeadlineOverruns, double? ProcessingP99Milliseconds,
    long? ReadyDsdBits, long? CapacityDsdBits, long? RenderUnderruns, long? MissingDsdBits);

internal static class NativeProcessingTelemetry
{
    internal static NativeProcessResources? Resources(JsonElement? processing)
    {
        if (Object(processing, "resourceUsage") is not { } resource
            || Count(resource, "pid") is not > 0
            || Count(resource, "workingSetBytes") is not { } working) return null;
        return new(Count(resource, "pid")!.Value, working, Count(resource, "privateBytes"));
    }

    internal static SdmRealtimePerformance? Sdm(JsonElement? processing)
    {
        if (Object(processing, "sdm") is not { } sdm
            || Object(sdm, "realtimeHealth") is not { } health) return null;
        var transport = Object(health, "transport");
        var bits = transport?.Text("unit") == "dsd-bits-per-channel";
        return new(health.Text("state") ?? "unknown", health.Text("stream") ?? "unknown",
            health.Flag("independentlyCalibrated"), health.Flag("executionMatchesObservation"),
            Count(health, "blocks"), Count(health, "deadlineOverruns"),
            Number(Object(health, "processingMs"), "p99"), bits ? Count(transport, "ready") : null,
            bits ? Count(transport, "capacity") : null, Count(transport, "renderCallbackUnderruns"),
            bits ? Count(transport, "renderCallbackMissingBits") : null);
    }

    private static JsonElement? Object(JsonElement? value, string name)
        => value is { ValueKind: JsonValueKind.Object } parent && parent.TryGetProperty(name, out var child)
            && child.ValueKind == JsonValueKind.Object ? child : null;
    private static long? Count(JsonElement? value, string name)
        => value is { ValueKind: JsonValueKind.Object } parent && parent.TryGetProperty(name, out var number)
            && number.ValueKind == JsonValueKind.Number && number.TryGetInt64(out var count) && count >= 0 ? count : null;
    private static double? Number(JsonElement? value, string name)
        => value is { ValueKind: JsonValueKind.Object } parent && parent.TryGetProperty(name, out var number)
            && number.ValueKind == JsonValueKind.Number && number.TryGetDouble(out var count)
            && double.IsFinite(count) && count >= 0 ? count : null;
}
