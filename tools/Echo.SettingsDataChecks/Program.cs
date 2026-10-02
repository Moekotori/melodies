using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Contracts.Settings;
using Echo.Infrastructure.Backup;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Preferences;

var directory = Path.GetFullPath(args.Length > 0 ? args[0] : "misc/settings-data-check");
Directory.CreateDirectory(directory);
var source = Path.Combine(directory, "source"); Directory.CreateDirectory(source);
await using var database = await LibraryDatabase.OpenAsync(Path.Combine(source, "library.sqlite"));
var job = await database.BeginScanAsync(source, false, CancellationToken.None);
var entry = new ScanEntry(new FileSnapshot(Path.Combine(source, "fixture.wav"), "fixture-path", 100, 1), ScanEntryKind.Metadata,
    new TrackMetadata("fixture", "artist", "album", "artist", 10, "pcm", 44100, 16));
await database.CommitScanBatchAsync(job, 1, [entry], CancellationToken.None);
await database.FinishScanAsync(job, ScanStatus.Completed, 1, false, null, CancellationToken.None);
if ((await database.CheckAsync(CancellationToken.None)).Integrity != "ok") throw new InvalidOperationException("SQLite integrity check failed.");
var profile = new DesktopPreferences(UiScalePercent: 115, Features: new() { ["probe"] = new(true) });
await SettingsTransfer.WriteAsync(Path.Combine(source, "preferences.json"), profile, CancellationToken.None);
if ((await SettingsTransfer.ReadAsync(Path.Combine(source, "preferences.json"), CancellationToken.None)).UiScalePercent != 115)
    throw new InvalidOperationException("Settings transfer failed.");
var archive = Path.Combine(directory, "backup.zip"); var backup = new LocalBackupService(source, database);
await backup.CreateAsync(archive, CancellationToken.None);
var manifest = await backup.ValidateAsync(archive, CancellationToken.None);
if (!manifest.Files.Any(file => file.Name == "library.sqlite") || !manifest.Files.Any(file => file.Name == "preferences.json"))
    throw new InvalidOperationException("Backup files were not captured.");
var request = await BackupRestore.StageAsync(archive, source, CancellationToken.None);
if (!File.Exists(request)) throw new InvalidOperationException("Restore staging failed.");
Console.WriteLine("Passed: real SQLite integrity, consistent backup, settings export/import parsing, archive hashes and restore staging.");
