namespace Echo.Contracts.Backup;
public sealed record BackupFile(string Name, long Length, string Sha256);
public sealed record BackupManifest(int Version, string CreatedAt, BackupFile[] Files);
public sealed record BackupSchedule(bool Enabled, string Directory, double Hours, int Retention);
public interface IDataBackup
{
    event Action<string>? Status;
    Task CreateAsync(string destination, CancellationToken token);
    Task<BackupManifest> ValidateAsync(string archivePath, CancellationToken token);
    Task ConfigureAsync(bool enabled, string directory, double hours, int retention, CancellationToken token);
    Task PrepareRestoreAsync(CancellationToken token);
}
