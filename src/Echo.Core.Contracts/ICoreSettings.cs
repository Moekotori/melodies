using Echo.Contracts.Settings;
namespace Echo.Core.Contracts;
public interface ICoreSettings
{
    Task<Dictionary<string, FeatureSetting>> ReadAsync(CancellationToken token);
    Task ConfirmAsync(string id, FeatureSetting value, CancellationToken token);
}
public static class CoreFeatureKeys
{
    public static bool Contains(string id) => id is "fixed-volume" or "gapless-playback" or "automix" or "speed-mode" or "scan-performance"
        or "auto-play-on-startup" or "prevent-sleep-while-playing" or "obs-browser-source" or "stage-api"
        or "automatic-backup-enabled" or "automatic-backup-hours" or "automatic-backup-retention" or "backup-directory";
}
