using System.Diagnostics;
using System.Text.Json;

namespace Echo.Playback;

public sealed record ProcessingStagePerformance(string Stage, long Timestamp, double? Speed,
    double? LastMilliseconds, double? PeakMilliseconds);
public sealed record PlaybackPerformance(long OperationId, long Timestamp, long HostEpoch,
    long? BufferedFrames, int? PcmSampleRate, bool? InputEnded,
    long? HostUnderrunCallbacks, long? HostUnderrunFrames,
    ProcessingStagePerformance? Src, ProcessingStagePerformance? Sdm)
{
    public NativeProcessResources? NativeResources { get; init; }
    public SdmRealtimePerformance? SdmRealtime { get; init; }
    public double? BufferedMilliseconds => BufferedFrames is { } frames && PcmSampleRate is > 0
        ? frames * 1000d / PcmSampleRate.Value : null;
    public static PlaybackPerformance Unknown { get; } = new(0, 0, 0, null, null, null, null, null, null, null);
}

public sealed partial class PlaybackSessionCoordinator
{
    private PlaybackPerformance performance = PlaybackPerformance.Unknown;
    private long? hostUnderruns, hostUnderrunFrames;
    private long hostMetricsEpoch = -1;

    private void ObservePerformance(JsonElement value, bool host = false)
    {
        if (hostMetricsEpoch != transportEpoch)
        { hostMetricsEpoch = transportEpoch; hostUnderruns = null; hostUnderrunFrames = null; }
        var now = Stopwatch.GetTimestamp();
        if (host)
        {
            hostUnderruns = Counter(value, "underrunCallbacks");
            hostUnderrunFrames = Counter(value, "underrunFrames");
            performance = performance with { HostEpoch = transportEpoch, HostUnderrunCallbacks = hostUnderruns,
                HostUnderrunFrames = hostUnderrunFrames };
            PublishState();
            return;
        }
        if (!value.TryGetProperty("bufferedFrames", out _) && value.Child("processing") is null) return;
        var previous = performance.OperationId == snapshot.OperationId ? performance : PlaybackPerformance.Unknown;
        var processing = value.Child("processing");
        // Buffered frames are output PCM frames. Other transports retain the count without an invented time conversion.
        var format = (processing ?? snapshot.Processing)?.Text("outputFormat")
            ?? snapshot.Output?.Child("processing")?.Text("outputFormat");
        var deviceFormat = snapshot.Output?.Text("format")?.ToLowerInvariant();
        var pcm = format == "pcm" || format is null && deviceFormat is "float32le" or "float32" or "f32" or "pcm" or "int16" or "int32";
        var rate = snapshot.Output is { } output ? Counter(output, "sampleRate") ?? 0 : 0;
        performance = new(snapshot.OperationId, now, transportEpoch, Counter(value, "bufferedFrames"),
            pcm && rate is > 0 and <= 768000 ? (int)rate : null,
            value.TryGetProperty("inputEnded", out var ended) && ended.ValueKind is JsonValueKind.True or JsonValueKind.False ? ended.GetBoolean() : null,
            hostUnderruns, hostUnderrunFrames,
            Stage(processing, "echoSrc", "SRC", now) ?? previous.Src,
            Stage(processing, "sdm", "SDM", now) ?? previous.Sdm)
        {
            NativeResources = NativeProcessingTelemetry.Resources(processing) ?? previous.NativeResources,
            SdmRealtime = NativeProcessingTelemetry.Sdm(processing) ?? previous.SdmRealtime
        };
    }

    private static long? Counter(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out var number) && number >= 0 ? number : null;

    private static ProcessingStagePerformance? Stage(JsonElement? processing, string field, string label, long now)
    {
        if (processing?.Child(field) is not { } stage) return null;
        if (!stage.Flag("active") || Counter(stage, "processedBlocks") is not > 0) return new(label, now, null, null, null);
        double? Number(string name) => stage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            && double.IsFinite(number) && number >= 0 ? number : null;
        var milliseconds = Number("lastProcessMilliseconds");
        var frames = Number("lastInputFrames");
        var rate = Number("sourceSampleRate");
        var speed = milliseconds is > 0 && frames is > 0 && rate is > 0 ? frames * 1000 / rate / milliseconds : null;
        if (speed is { } ratio && !double.IsFinite(ratio)) speed = null;
        return new(label, now, speed, milliseconds, Number("peakProcessMilliseconds"));
    }

    private PlaybackPerformance CurrentPerformance() => snapshot.OperationId > 0
        && snapshot.OperationId == performance.OperationId && performance.HostEpoch == transportEpoch
        ? performance : PlaybackPerformance.Unknown with { HostEpoch = transportEpoch,
            HostUnderrunCallbacks = hostMetricsEpoch == transportEpoch ? hostUnderruns : null,
            HostUnderrunFrames = hostMetricsEpoch == transportEpoch ? hostUnderrunFrames : null };
}
