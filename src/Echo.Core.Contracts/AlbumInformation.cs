namespace Echo.Core.Contracts;

public sealed record AlbumIdentity(string Title, string Artist, int? Year = null, int TrackCount = 0, string Language = "zh");
public sealed record AlbumInformationRequest(AlbumIdentity Album, bool Refresh = false);
public sealed record InformationLink(string Provider, string Label, string Url, string Kind = "资料");
public sealed record AlbumRelease(string Id, string Title, string Artist, string? Date, string? Country,
    string? Status, string? Barcode, string[] Formats, string[] Labels, string[] CatalogNumbers,
    string Url, bool Selected = false, int? TrackCount = null);
public sealed record ContributorGroup(string Role, Contributor[] People);
public sealed record Contributor(string Name, string? Url, string? Detail = null);
public sealed record InformationArticle(string Title, string Text, string Language, string Url,
    string? Description = null, string? ImageUrl = null);
public sealed record AlbumInformationResult(string Status, DateTimeOffset FetchedAt, string[] Sources,
    string? MatchTitle, string? MatchArtist, double? Confidence, bool Ambiguous, AlbumRelease? Release,
    AlbumRelease[] Versions, ContributorGroup[] Credits, InformationLink[] Links,
    InformationArticle? AlbumArticle, InformationArticle? ArtistArticle, string[] Errors)
{
    public static AlbumInformationResult Empty => new("empty", DateTimeOffset.UtcNow, [], null, null, null, false, null, [], [], [], null, null, []);
}
public interface IAlbumInformationClient
{
    Task<AlbumInformationResult> ReadAsync(AlbumInformationRequest request, CancellationToken cancellation = default);
}
