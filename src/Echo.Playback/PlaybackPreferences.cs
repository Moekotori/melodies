using System.Text.Json.Nodes;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private bool volumeLocked;
    private string rateMode = "speed";
    public bool VolumeLocked => Volatile.Read(ref volumeLocked);
    public string PlaybackRateMode => Volatile.Read(ref rateMode);
    public event Action<bool>? VolumeLockChanged;

    public Task SetVolumeLockedAsync(bool locked, CancellationToken token = default) => Command(() =>
    {
        Volatile.Write(ref volumeLocked, locked);
        if (VolumeLockChanged is { } handlers)
            foreach (Action<bool> handler in handlers.GetInvocationList()) try { handler(locked); } catch { }
        return Task.CompletedTask;
    }, token, affectsPlayback: false);

    public Task SetPlaybackRateModeAsync(string mode, CancellationToken token = default)
    {
        if (mode is not ("speed" or "nightcore")) throw new ArgumentException("不支持的变速模式。");
        return Command(async () =>
        {
            var parameters = Params(new JsonArray(new JsonObject { ["rate"] = Snapshot.PlaybackRate, ["mode"] = mode }));
            if (initialized)
            {
                var result = await native.CallAsync("playbackRate.configure", parameters).ConfigureAwait(false);
                ConfirmPlaybackRate(result);
            }
            else Volatile.Write(ref rateMode, mode); // A preference does not open an output device.
            dspCommands["playbackRate.configure:-1"] = parameters;
        }, token, affectsPlayback: false);
    }
}
