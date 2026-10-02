using Echo.Contracts.Library;

namespace Echo.Desktop.Bootstrap;

internal sealed partial class DesktopLibrary : ILibraryMaintenance
{
    public async Task<LibraryHealth> CheckAsync(CancellationToken cancellation)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).CheckAsync(cancellation).ConfigureAwait(false);
    public async Task BackupAsync(string destination, CancellationToken cancellation)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).BackupAsync(destination, cancellation).ConfigureAwait(false);
}
