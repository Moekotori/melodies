using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.Settings.Models;
using Echo.Desktop.Features.Settings.Runtime;
using Echo.Infrastructure.Backup;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Preferences;

internal static class SettingsFunctionalSmoke
{
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var state = new FeaturePreferences(); using var runtime = new SettingsFeatureRuntime(state);
        runtime.RegisterChange("feature-probe", (_, _) => Task.FromException(new IOException("Expected rejected operation")));
        var row = new SettingRowViewModel(new("feature-probe", "Probe"), true) { BoolValue = true };
        await runtime.ChangeAsync(row);
        if (state.Read("feature-probe", new()).Enabled) throw new InvalidOperationException("Rejected settings were persisted.");
        var source = Path.Combine(directory, "source"); Directory.CreateDirectory(source);
        var databasePath = Path.Combine(source, "library.sqlite");
        await using var database = await LibraryDatabase.OpenAsync(databasePath);
        var job = await database.BeginScanAsync(source, false, CancellationToken.None);
        var entry = new ScanEntry(new FileSnapshot(Path.Combine(source, "fixture.wav"), "fixture-path", 100, 1), ScanEntryKind.Metadata,
            new TrackMetadata("fixture", "artist", "album", "artist", 10, "pcm", 44100, 16));
        await database.CommitScanBatchAsync(job, 1, [entry], CancellationToken.None);
        await database.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, CancellationToken.None);
        if ((await database.CheckAsync(CancellationToken.None)).Integrity != "ok") throw new InvalidOperationException("Real database integrity check failed.");
        var profile = new DesktopPreferences(UiScalePercent: 115, Features: new() { ["probe"] = new(true) });
        await SettingsTransfer.WriteAsync(Path.Combine(source, "preferences.json"), profile, CancellationToken.None);
        if ((await SettingsTransfer.ReadAsync(Path.Combine(source, "preferences.json"), CancellationToken.None)).UiScalePercent != 115)
            throw new InvalidOperationException("Settings transfer failed.");
        var archive = Path.Combine(directory, "backup.zip"); var service = new LocalBackupService(source, database);
        await service.CreateAsync(archive, CancellationToken.None);
        var manifest = await service.ValidateAsync(archive, CancellationToken.None);
        if (!manifest.Files.Any(file => file.Name == "library.sqlite") || !manifest.Files.Any(file => file.Name == "preferences.json"))
            throw new InvalidOperationException("Backup did not contain real database and preferences.");
        var stagedRequest = await BackupRestore.StageAsync(archive, source, CancellationToken.None);
        if (!File.Exists(stagedRequest)) throw new InvalidOperationException("Restore staging did not create a request.");
        Console.WriteLine("Functional settings: rejected-change rollback, real SQLite health/consistent backup, JSON transfer, hash validation and restore staging passed.");
    }
}
