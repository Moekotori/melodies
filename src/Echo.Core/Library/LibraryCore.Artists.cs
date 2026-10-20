using Echo.Contracts.Library;
using Echo.ArtistMetadata;

namespace Echo.Core.Library;

public sealed partial class LibraryCore
{
    private ArtistInformationService? _artistInformation;
    internal Task<byte[]?> ReadArtistImageAsync(string reference, CancellationToken token)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _artistInformation ??= new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "artist-information"));
            return _artistInformation.ReadImageAsync(reference, token);
        }
    }
    public async Task<ArtistInsights> GetArtistInsightsAsync(string id, ArtistGrouping grouping, CancellationToken cancellation = default)
        => await (await Database().WaitAsync(cancellation).ConfigureAwait(false)).GetArtistInsightsAsync(id, grouping, cancellation).ConfigureAwait(false);
    public async Task<ArtistInformation> GetArtistInformationAsync(ArtistInformationRequest request, CancellationToken cancellation = default)
    {
        ArtistInformationService service;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _artistInformation ??= new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "artist-information"));
            service = _artistInformation;
        }
        var information = await service.GetArtistInformationAsync(request, cancellation).ConfigureAwait(false);
        if (information.Relations.Count == 0 && (information.PublicFacts?.Members.Count ?? 0) == 0) return information;
        var database = await Database().WaitAsync(cancellation).ConfigureAwait(false);
        var matches = new Dictionary<string, ArtistRow?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in information.Relations.Take(12).Concat(information.PublicFacts?.Members.Take(16) ?? []).Select(relation => relation.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var page = await database.GetArtistsAsync(new(Search: name, Limit: 8), cancellation).ConfigureAwait(false);
            matches[name] = page.Items.FirstOrDefault(artist => artist.Name.Trim().Normalize().Equals(name.Trim().Normalize(), StringComparison.OrdinalIgnoreCase));
        }
        ArtistOnlineRelation Match(ArtistOnlineRelation relation) => relation with { LocalArtist = matches.GetValueOrDefault(relation.Name) };
        return information with
        {
            Relations = information.Relations.Take(12).Select(Match).ToArray(),
            PublicFacts = information.PublicFacts is { } facts ? facts with { Members = facts.Members.Take(16).Select(Match).ToArray() } : null
        };
    }
}
