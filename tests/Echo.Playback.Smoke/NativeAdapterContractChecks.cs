using System.Text.Json;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class NativeAdapterContractChecks
{
    public static void Run()
    {
        using var previous = JsonDocument.Parse("""
            {"sharedBackend":"pipewire","latencyProfile":"stable","processing":{"outputFormat":"pcm",
            "echoSrc":{"sourceSampleRate":48000,"targetSampleRate":96000,"stages":[{"upsampleFactor":2,"taps":[0.1,0.8,0.1]}]},
            "dither":{"mode":"tpdf","bitDepth":24}}}
            """);
        var preserved = new AudioOutputConfiguration(BufferFrames: 0).PreserveProcessingFrom(previous.RootElement).ToJson();
        Require(preserved.Long("bufferSize") == 0 && preserved.Text("sharedBackend") == "pipewire"
            && preserved.Child("processing")?.Child("echoSrc")?.Long("targetSampleRate") == 96000,
            "Device-managed buffer and advanced output preservation");
        Require(new AudioOutputConfiguration("asio", Format: "dsd-native-raw", DsdTransportRate: 352800).ToJson().Long("sampleRate") == 5644800,
            "Native DSD carrier/bit-rate conversion");

        using var telemetry = JsonDocument.Parse("""
            {"resourceUsage":{"pid":123,"workingSetBytes":100000,"privateBytes":null},"sdm":{"realtimeHealth":{
            "state":"at-risk","stream":"running","independentlyCalibrated":false,"executionMatchesObservation":true,
            "blocks":12,"deadlineOverruns":2,"processingMs":{"p99":4.5},
            "transport":{"unit":"dsd-bits-per-channel","ready":1024,"capacity":4096,
            "renderCallbackUnderruns":3,"renderCallbackMissingBits":64}}}}
            """);
        var resources = NativeProcessingTelemetry.Resources(telemetry.RootElement);
        var sdm = NativeProcessingTelemetry.Sdm(telemetry.RootElement);
        Require(resources is { ProcessId: 123, WorkingSetBytes: 100000, PrivateBytes: null }
            && sdm is { State: "at-risk", IndependentlyCalibrated: false, ReadyDsdBits: 1024,
                RenderUnderruns: 3, MissingDsdBits: 64, ProcessingP99Milliseconds: 4.5 }, "Native telemetry units and unknown facts");
        using var oldReady = JsonDocument.Parse("""
            {"protocolVersion":1,"backendContractVersion":2,"capabilities":{
            "hostOwnedLocalPlaybackV1":true,"runtimeDeviceConfigureV1":true,"nativeDspV1":true}}
            """);
        try { NativeHostProtocol.Validate(oldReady.RootElement); }
        catch (IOException) { Console.WriteLine("PASS output preservation, native DSD units, native telemetry and stale-host rejection"); return; }
        throw new Exception("Stale native host was accepted");
    }

    private static void Require(bool valid, string description)
    { if (!valid) throw new Exception(description); }
}
