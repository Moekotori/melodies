using Echo.Application.Scanning;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Metadata;

namespace Echo.Core.Library;
public sealed partial class LibraryCore
{
    internal Task<ScanReport> ScanRequestAsync(ScanRequest request, string? scanner, IProgress<ScanProgress> progress, CancellationToken token)
    {
        if (request.Workers is < 1 or > 4 || request.BatchSize is < 1 or > 256) throw new ArgumentException("扫描并发或批次超出 Core 预算。");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_scan is { IsCompleted: false }) throw new InvalidOperationException("A scan is already active.");
            return _scan = Task.Run(async () =>
            {
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
                var database = await Database().WaitAsync(lifetime.Token).ConfigureAwait(false);
                var coordinator = new ScanCoordinator(database, new LegacyFolderDiscovery(database), () => new LocalMetadataReader(scanner), admission);
                return await coordinator.ScanAsync(request, progress, lifetime.Token).ConfigureAwait(false);
            });
        }
    }
}
