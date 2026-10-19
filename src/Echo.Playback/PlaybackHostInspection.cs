using Echo.Playback.NativeAudio;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    public async Task<NativeHostFeatures> InspectNativeHostAsync(CancellationToken token = default)
    {
        NativeHostFeatures? features = null;
        await Command(async () =>
        {
            await EnsureOutputQueryHostAsync().ConfigureAwait(false);
            if (native is not INativeHostInspection inspection) throw new NotSupportedException("宿主不支持构建能力查询");
            features = await inspection.InspectAsync(token).ConfigureAwait(false);
        }, token, affectsPlayback: false).ConfigureAwait(false);
        return features!;
    }
}
