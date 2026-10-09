using System.Text.Json;
using Echo.Playback;

namespace Echo.Desktop.Features.Connect.Output;

internal static class ConnectOutputRequest
{
    public static AudioOutputConfiguration Create(JsonElement? current, ConnectDeviceItem item)
    {
        var rate = item.Device.SampleRate is >= 8000 and <= 768000 ? item.Device.SampleRate : 48000;
        var fallback = new AudioOutputConfiguration(item.Mode, item.Device, SampleRate: rate);
        if (current is not { ValueKind: JsonValueKind.Object } config) return fallback;
        if (!config.TryGetProperty("outputMode", out var mode) || mode.GetString() != item.Mode) return fallback;
        var format = config.TryGetProperty("processing", out var processing) && processing.TryGetProperty("outputFormat", out var outputFormat)
            ? outputFormat.GetString() ?? "pcm" : "pcm";
        var storedRate = Int(config, "sampleRate", rate);
        var built = new AudioOutputConfiguration(item.Mode, item.Device, Int(config, "channels", 2), format == "pcm" ? storedRate : 48000,
            Int(config, "bufferSize", 1024), Int(config, "asioOutputChannelStart", 0), format, format == "pcm" ? 176400 : storedRate);
        try { built.Validate(); return built; }
        catch (ArgumentException) { return fallback; }
    }

    private static int Int(JsonElement config, string name, int fallback)
        => config.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;
}
