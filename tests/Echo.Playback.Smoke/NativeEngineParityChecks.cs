using System.Text.Json;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class NativeEngineParityChecks
{
    // Prevent source-only imports from hiding a stale binary or blocked C# DSP lane.
    public static async Task RunAsync(PlaybackSessionCoordinator player, NativeAudioHost host)
    {
        var features = await player.InspectNativeHostAsync();
        if (!features.SdmRealtimeHealth || !features.BlindEqPreparation)
            throw new Exception("New native build capabilities are missing");
        await player.ConfigureOutputAsync(new AudioOutputConfiguration(BufferFrames: 0));
        using var legacy = JsonDocument.Parse("""
            [{"order":["equalizer","convolution","replayGain","compressor","crossfeed","stereoField","channelMatrix","channelBalance"]}]
            """);
        var rack = await player.DspAsync("dspRack.setState", legacy.RootElement, retainForRecovery: true);
        if (rack.GetProperty("order").GetArrayLength() != 11)
            throw new Exception("Legacy rack settings were not migrated to the native rack");
        if (host.HostIdentity is not { } identity
            || !identity.TryGetProperty("sourceDigest", out var digest)
            || digest.GetString()?.Length != 64)
            throw new Exception("Native production build identity was not received");
        using var empty = JsonDocument.Parse("[]");
        using var disabled = JsonDocument.Parse("[{\"enabled\":false}]");
        foreach (var module in new[] { "multiband", "deesser", "virtualStage", "headphoneCorrection" })
        {
            var before = await player.DspAsync(module + ".getState", empty.RootElement);
            if (before.ValueKind != JsonValueKind.Object)
                throw new Exception(module + " native state is missing");
            var request = disabled.RootElement;
            using var complete = module == "headphoneCorrection"
                ? JsonDocument.Parse("[" + before.GetRawText() + "]") : null;
            if (complete is not null) request = complete.RootElement;
            var result = await player.DspAsync(module + ".setState", request, retainForRecovery: true);
            if (result.ValueKind != JsonValueKind.Object)
                throw new Exception(module + " native configuration was not acknowledged");
        }
        Console.WriteLine("PASS native build identity and C# multiband/deesser/virtualStage/headphoneCorrection control receipts");
    }
}
