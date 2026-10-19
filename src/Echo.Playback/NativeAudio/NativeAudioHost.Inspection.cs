using System.Text.Json;

namespace Echo.Playback.NativeAudio;

public sealed partial class NativeAudioHost
{
    public async Task<NativeHostFeatures> InspectAsync(CancellationToken token = default)
    {
        await StartAsync(token).ConfigureAwait(false);
        var identity = HostIdentity ?? throw new IOException("音频宿主身份未确认");
        var text = await NativeOutputDiscovery.RunAsync(executable, ["--build-capabilities"], token).ConfigureAwait(false);
        using var json = JsonDocument.Parse(text);
        var features = json.RootElement;
        if (features.Child("hostIdentity")?.Text("buildId") != identity.Text("buildId"))
            throw new IOException("磁盘音频宿主与运行中的版本不同，请停止播放后重建宿主");
        return new(identity.Text("buildId")!, identity.Text("sourceDigest")!, features.Flag("nativeCudaDspV1"),
            identity.Child("compiledCapabilities")?.Flag("sdmRuntimeHealthV1") == true,
            features.Flag("blindEqPreparationV1"), features.Flag("deviceVolumeV1"), features.Flag("eac3HdmiV1"));
    }
}
