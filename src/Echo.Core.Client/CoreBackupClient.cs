using Echo.Contracts.Backup;
using Echo.Core.Contracts;
using Echo.Core.Transport;
namespace Echo.Core.Client;
internal sealed class CoreBackupClient(CoreWireClient wire, CoreEndpoint endpoint) : IDataBackup
{
    public event Action<string>? Status { add { } remove { } }
    public async Task CreateAsync(string destination, CancellationToken token)
    {
        var accepted = await wire.CallAsync<CoreTaskStart>("tasks.start", CoreJson.Value(new CoreTaskRequest("", Id: Guid.NewGuid(), BackupPath: destination)), token).ConfigureAwait(false);
        await CoreTaskAwaiter.WaitAsync(wire, accepted.Id, null, token).ConfigureAwait(false);
    }
    public Task<BackupManifest> ValidateAsync(string archivePath, CancellationToken token)
        => wire.CallAsync<BackupManifest>("backup.validate", CoreJson.Value(archivePath), token);
    public Task ConfigureAsync(bool enabled, string directory, double hours, int retention, CancellationToken token)
        => wire.CallAsync("backup.configure", CoreJson.Value(new BackupSchedule(enabled, directory, hours, retention)), token);
    public async Task PrepareRestoreAsync(CancellationToken token)
    {
        await wire.CallAsync("core.shutdown", CoreJson.Empty, token).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try { using var ownership = new FileStream(endpoint.LibraryPath + ".core.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); return; }
            catch (IOException) { await Task.Delay(100, timeout.Token).ConfigureAwait(false); }
        }
    }
}
