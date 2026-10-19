using Echo.Contracts.Backup;
using Echo.Infrastructure.Backup;

namespace Echo.Core;
public sealed partial class CoreRuntime
{
    internal CoreSettingsStore Settings { get; private set; } = null!;
    internal LocalBackupService Backup { get; private set; } = null!;
    internal AutomaticBackupService AutomaticBackup { get; private set; } = null!;
    internal bool HasScheduledBackup => Settings.Read("automatic-backup-enabled", new()).Enabled;
    private void InitializeBackup(string libraryPath, CoreRuntimeOptions? options)
    {
        Settings = new(options);
        Backup = new(options?.Directory ?? Path.GetDirectoryName(Path.GetFullPath(libraryPath))!, Library);
        AutomaticBackup = new(Backup);
    }
    internal async Task RestoreCoreSettingsAsync(CancellationToken token)
    {
        var session = Playback.LocalZone.Session;
        await session.SetVolumeLockedAsync(Settings.Read("fixed-volume", new()).Enabled, token).ConfigureAwait(false);
        await session.SetAlbumGaplessEnabledAsync(Settings.Read("gapless-playback", new()).Enabled, token).ConfigureAwait(false);
        await session.SetAutomixEnabledAsync(Settings.Read("automix", new()).Enabled, token).ConfigureAwait(false);
        await session.SetPlaybackRateModeAsync(Settings.Read("speed-mode", new(false, "Doubletime")).Selection == "Nightcore" ? "nightcore" : "speed", token).ConfigureAwait(false);
        var profile = Settings.Read("scan-performance", new(false, "均衡")).Selection;
        await Library.SetScanProfileAsync(profile is "低占用" or "均衡" or "高性能" or "极速" ? profile : "均衡", token).ConfigureAwait(false);
        await Broadcast.SetEnabledAsync(Settings.Read("obs-browser-source", new()).Enabled || Settings.Read("stage-api", new()).Enabled, token).ConfigureAwait(false);
        await ConfigureBackupAsync(new(HasScheduledBackup, Settings.Read("backup-directory", new()).Selection,
            double.TryParse(Settings.Read("automatic-backup-hours", new(false, "24")).Selection, out var hours) ? hours : 24,
            int.TryParse(Settings.Read("automatic-backup-retention", new(false, "5")).Selection, out var retention) ? retention : 5), token).ConfigureAwait(false);
        if (Settings.Read("auto-play-on-startup", new()).Enabled && session.Snapshot.Status == Echo.Playback.PlaybackStatus.Paused)
        {
            try { await session.ResumeAsync(token).ConfigureAwait(false); }
            catch (Exception) when (!token.IsCancellationRequested) { /* Playback publishes its fault; the library service stays available. */ }
        }
    }
    internal async Task ConfigureBackupAsync(BackupSchedule schedule, CancellationToken token)
    {
        await AutomaticBackup.ConfigureAsync(schedule.Enabled, schedule.Directory, schedule.Hours, schedule.Retention, token).ConfigureAwait(false);
        Settings.Confirm("automatic-backup-enabled", new(schedule.Enabled));
        Settings.Confirm("backup-directory", new(Selection: schedule.Directory));
        Settings.Confirm("automatic-backup-hours", new(Selection: schedule.Hours.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Settings.Confirm("automatic-backup-retention", new(Selection: schedule.Retention.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}
