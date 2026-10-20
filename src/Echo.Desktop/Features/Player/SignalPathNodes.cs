using System.Globalization;
using System.Text.Json;
using Echo.Playback;
using static Echo.Desktop.Features.Player.SignalPathFacts;

namespace Echo.Desktop.Features.Player;

// Builds the chain from confirmed native telemetry. Every fact comes from a reported field; nothing is inferred when a field is absent.
internal static class SignalPathNodes
{
    public sealed class Input
    {
        public required Func<string, string> Text { get; init; }
        public required PlaybackSnapshot State { get; init; }
        public required JsonElement? Output { get; init; }
        public required JsonElement? Processing { get; init; }
        public required string Format { get; init; }
        public required string Spec { get; init; }
        public required double? SourceRate { get; init; }
        public required int BitDepth { get; init; }
        public required bool Active { get; init; }
        public required bool Dsp { get; init; }
        public required bool Resampled { get; init; }
        public required bool Attenuated { get; init; }
        public required bool Shared { get; init; }
        public required string Device { get; init; }
        public required string Mode { get; init; }
        public required string Clock { get; init; }
        public required bool ClockChanged { get; init; }
        public required double? Rate { get; init; }
    }

    private static readonly HashSet<string> Lossy = ["MP3", "AAC", "OGG", "OPUS", "VORBIS", "WMA"];

    public static List<SignalPathNode> Create(Input i)
    {
        var L = i.Text;
        var state = i.State;
        var src = Child(i.Processing, "echoSrc"); var sdm = Child(i.Processing, "sdm");
        var dither = Child(i.Processing, "dither"); var limiter = Child(i.Processing, "limiter");
        var srcOn = Flag(src, "active"); var sdmOn = Flag(sdm, "active");
        var faulted = state.Status == PlaybackStatus.Faulted;
        var origin = state.Current?.Source?.Kind == "http" ? L("http") : L("local");
        var chain = new List<SignalPathNode>
        {
            new("music", L("source"), i.Spec, origin, Tone: state.Current is null ? "muted" : "good", Tag: Quality(i),
                Facts: Rows((L("file"), state.Current is { FilePath.Length: > 0 } current ? Path.GetFileName(current.FilePath) : null),
                    (L("duration"), Duration(state.Current?.Metadata?.GetValueOrDefault("duration"))))),
            new("waves", L("decode"), "PCM", L("hostDecode"), Tone: i.Active ? "good" : "muted",
                Facts: Rows((L("sampleFormat"), Text(i.Processing, "outputFormat") ?? Text(i.Output, "format")),
                    (L("channels"), Channels(i.Output))))
        };
        if (i.Dsp)
            chain.Add(Stage("sliders", L("stageDsp"), L("dspRoute"), L("stageDspNote"), null));
        if (srcOn)
            chain.Add(Stage("sliders", L("src"), Convert(Number(src, "sourceSampleRate"), Number(src, "targetSampleRate")), "melodies SRC",
                Rows((L("stages"), Count(Number(src, "stageCount"))),
                    (L("compute"), Text(src, "activeBackend")?.ToUpperInvariant()),
                    (L("chipLatency"), Ms(Number(src, "nominalLatencyMilliseconds"))),
                    (L("avgProcess"), Ms(Number(src, "averageProcessMilliseconds"))),
                    (L("fallback"), Text(src, "fallbackReason")))));
        else if (i.Resampled && !sdmOn)
            chain.Add(Stage("sliders", L("stageDevice"), Convert(i.SourceRate, Number(i.Output, "sampleRate")), L("stageDeviceNote"), null));
        if (sdmOn)
            chain.Add(Stage("sliders", "PCM → DSD", Convert(Number(sdm, "sourceSampleRate"), Number(sdm, "targetSampleRate")), L("insightSdmAdvice"),
                Rows((L("compute"), Text(sdm, "activeBackend")?.ToUpperInvariant()),
                    (L("order"), Count(Number(sdm, "modulatorOrder"))),
                    (L("decision"), Text(sdm, "decisionMode")),
                    (L("fallback"), Text(sdm, "fallbackReason")))));
        if (i.Attenuated)
            chain.Add(Stage("sliders", L("volume"), $"{state.Volume:P0}", L("stageVolumeNote"),
                Rows((L("gainDb"), state.Volume > 0 ? Db(20 * Math.Log10(state.Volume)) : null))));
        if (Flag(limiter, "active") || Flag(limiter, "protecting"))
            chain.Add(Stage("sliders", L("stageLimiter"), Flag(limiter, "protecting") ? L("insightLimiter") : L("on"), L("insightLimiterDetail"),
                Rows((L("ceilingDb"), Db(Number(limiter, "ceilingDb"))), (L("gainReduction"), Db(Number(limiter, "gainReductionDb"))))));
        if (Flag(dither, "active"))
            chain.Add(Stage("sliders", L("dither"), Text(dither, "mode") ?? L("on"), "",
                Rows((L("bitDepth"), Number(dither, "bitDepth") is > 0 and var bits ? $"{bits:0} bit" : null))));
        var buffer = Number(i.Output, "actualBufferFrames") ?? Number(i.Output, "deviceBufferFrames");
        var bufferText = buffer is > 0 ? Join($"{buffer:0} frames", i.Rate is > 0 ? Ms(buffer / i.Rate * 1000) : null) : null;
        chain.Add(new("headphones", L("output"), i.Device, Join(i.Mode, Rate(i.Rate)), IsLast: true,
            Tone: faulted ? "danger" : !i.Active ? "muted" : i.Shared ? "warn" : "good",
            Facts: Rows((L("backend"), Text(i.Output, "backendImpl") ?? Text(i.Output, "backend")),
                (L("sampleFormat"), Text(i.Output, "format")), (L("channels"), Channels(i.Output)),
                (L("buffer"), bufferText), (L("clock"), i.ClockChanged ? i.Clock : null))));
        return chain;
    }

    private static SignalPathNode Stage(string icon, string title, string value, string detail, IReadOnlyList<SignalPathFact>? facts) =>
        new(icon, title, value, detail, IsProcessing: true, Tone: "proc", Facts: facts);

    private static string Quality(Input i)
    {
        if (i.Format.Length == 0 && i.SourceRate is not > 0) return "";
        return Lossy.Contains(i.Format.ToUpperInvariant()) ? i.Text("tagLossy")
            : i.SourceRate > 48000 || i.BitDepth > 16 ? i.Text("tagHiRes") : i.Text("tagLossless");
    }

    private static IReadOnlyList<SignalPathFact> Rows(params (string Label, string? Value)[] items) =>
        items.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => new SignalPathFact(x.Label, x.Value!)).ToArray();
    private static string Convert(double? from, double? to) => from is > 0 && to is > 0 ? $"{Rate(from)} → {Rate(to)}" : "";
    private static string? Count(double? value) => value is > 0 ? value.Value.ToString("0", CultureInfo.InvariantCulture) : null;
    private static string? Channels(JsonElement? output) => Count(Number(output, "channels"));
    private static string? Ms(double? value) => value is > 0 ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) + " ms" : null;
    private static string? Db(double? value) => value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) + " dB" : null;
    private static string? Duration(string? seconds) => double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0
        ? TimeSpan.FromSeconds(s).ToString(s >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture) : null;
}
