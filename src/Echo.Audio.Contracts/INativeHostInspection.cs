using System.Text.Json;

namespace Echo.Playback.NativeAudio;

public sealed record NativeHostFeatures(string BuildId, string SourceDigest, bool CudaDsp,
    bool SdmRealtimeHealth, bool BlindEqPreparation, bool HardwareVolume, bool Eac3Hdmi);

public interface INativeHostInspection
{
    JsonElement? HostIdentity { get; }
    Task<NativeHostFeatures> InspectAsync(CancellationToken token = default);
}
