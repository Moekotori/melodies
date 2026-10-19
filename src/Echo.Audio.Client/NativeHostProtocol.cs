using System.Text.Json;

namespace Echo.Playback.NativeAudio;

internal static class NativeHostProtocol
{
    internal static void Validate(JsonElement ready)
    {
        if (ready.Long("protocolVersion") != 1 || ready.Long("backendContractVersion") != 2
            || ready.Child("capabilities") is not { } caps || !caps.Flag("hostOwnedLocalPlaybackV1")
            || !caps.Flag("runtimeDeviceConfigureV1") || !caps.Flag("nativeDspV1"))
            throw new IOException("Native Audio Host 协议或播放能力不匹配");
        if (ready.Child("hostIdentity") is not { } identity || identity.Long("schemaVersion") != 1
            || identity.Text("imageRole") != "echo-audio-host" || identity.Text("buildId")?.Length != 64
            || identity.Text("sourceDigest")?.Length != 64
            || identity.Long("protocolVersion") != 1 || identity.Long("backendContractVersion") != 2)
            throw new IOException("音频宿主版本过旧或角色不匹配，请为当前平台重新构建音频宿主");
    }
}
