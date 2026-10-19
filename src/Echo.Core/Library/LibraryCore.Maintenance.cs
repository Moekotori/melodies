using Echo.Contracts.Library;

namespace Echo.Core.Library;

public sealed partial class LibraryCore : ILibraryMaintenance
{
    public async Task<LibraryHealth> CheckAsync(CancellationToken cancellation)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).CheckAsync(cancellation).ConfigureAwait(false);
    public async Task BackupAsync(string destination, CancellationToken cancellation)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).BackupAsync(destination, cancellation).ConfigureAwait(false);
}
