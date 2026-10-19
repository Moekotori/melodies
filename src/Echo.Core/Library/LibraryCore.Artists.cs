using Echo.Contracts.Library;
using Echo.ArtistMetadata;

namespace Echo.Core.Library;

public sealed partial class LibraryCore
{
    private ArtistInformationService? _artistInformation;
    public async Task<ArtistInsights> GetArtistInsightsAsync(string id, ArtistGrouping grouping, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetArtistInsightsAsync(id, grouping, cancellation).ConfigureAwait(false);
    public Task<ArtistInformation> GetArtistInformationAsync(ArtistInformationRequest request, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _artistInformation ??= new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "artist-information"));
            return _artistInformation.GetArtistInformationAsync(request, cancellation);
        }
    }
}
