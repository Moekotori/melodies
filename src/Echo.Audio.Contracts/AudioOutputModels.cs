using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback;

public sealed record AudioOutputDevice(string Id, string Name, string Mode, int Index, bool IsDefault,
    int SampleRate, IReadOnlyList<int> NativeDsdSampleRates);
public sealed record AudioOutputMode(string Key, string Label);

public interface INativeOutputControl
{
    int OutputChannels { get; }
    bool ProDspAllowed { get; }
    JsonElement? Capabilities { get; }
    Task SetOutputChannelsAsync(int channels, CancellationToken token = default, bool forceRestart = false);
    Task<IReadOnlyList<AudioOutputDevice>> EnumerateOutputDevicesAsync(string mode, CancellationToken token = default);
    Task<JsonElement> ProbeAsioDsdAsync(AudioOutputDevice device, CancellationToken token = default);
}

public sealed record AudioOutputConfiguration(string Mode = "shared", AudioOutputDevice? Device = null,
    int Channels = 2, int SampleRate = 48000, int BufferFrames = 1024, int AsioChannelStart = 0,
    string Format = "pcm", int DsdTransportRate = 176400)
{
    public void Validate()
    {
        if (Mode is not ("shared" or "exclusive" or "asio" or "ks")) throw new ArgumentException("无效的输出模式");
        if (Device is not null && Device.Mode != Mode) throw new ArgumentException("设备与输出模式不匹配");
        if (Channels is < 2 or > 8 || Channels > 2 && (Mode != "asio" || Format != "pcm"))
            throw new ArgumentException("2–8 声道 PCM 需要 ASIO；DSD 使用双声道");
        if (SampleRate is < 8000 or > 768000 || BufferFrames is < 64 or > 32768 || AsioChannelStart is < 0 or > 128)
            throw new ArgumentException("无效的采样率、缓冲或声道起点");
        if (Format is not ("pcm" or "dop24le" or "dsd-native-raw")) throw new ArgumentException("无效的输出格式");
        if (Format == "dop24le" && Mode is not ("asio" or "exclusive") || Format == "dsd-native-raw" && Mode != "asio")
            throw new ArgumentException("DoP 需要独占/ASIO；Native DSD 需要 ASIO");
        if (Format != "pcm" && DsdTransportRate is not (176400 or 192000 or 352800 or 384000 or 705600 or 768000 or 1411200 or 1536000))
            throw new ArgumentException("无效的 DSD 传输率");
    }
    public JsonElement ToJson()
    {
        Validate();
        var node = new JsonObject
        {
            ["outputMode"] = Mode, ["deviceId"] = Device?.Id ?? "", ["deviceIndex"] = Device?.Index ?? -1,
            ["deviceName"] = string.IsNullOrEmpty(Device?.Id) ? "" : Device.Name, ["channels"] = Channels,
            ["sampleRate"] = Format == "pcm" ? SampleRate : Format == "dsd-native-raw" ? DsdTransportRate * 16 : DsdTransportRate,
            ["bufferSize"] = BufferFrames, ["asioOutputChannelStart"] = AsioChannelStart,
            ["processing"] = new JsonObject { ["outputFormat"] = Format }
        };
        if (Format != "pcm")
            node["processing"]!["sdm"] = new JsonObject { ["qualityProfile"] = "safe", ["sourceSampleRate"] = DsdTransportRate,
                ["targetSampleRate"] = DsdTransportRate, ["sampleRateFamily"] = DsdTransportRate % 44100 == 0 ? "44.1k" : "48k", ["computeBackend"] = "cpu" };
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
