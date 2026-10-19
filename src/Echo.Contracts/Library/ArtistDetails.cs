namespace Echo.Contracts.Library;

public sealed record ArtistInsights(long Revision, int FavoriteTracks, int? FirstYear, int? LastYear,
    long Plays, double ListenedSeconds, long? LastPlayedUnix, int UnplayedAlbums,
    IReadOnlyList<ListeningTrack> TopTracks, IReadOnlyList<ArtistRelation> RelatedArtists,
    IReadOnlyList<TrackRow> Collaborations, IReadOnlyList<AlbumRow> DiscoverAlbums);
public sealed record ArtistRelation(ArtistRow Artist, int SharedTracks);
public sealed record ArtistInformationRequest(string Name, string Language = "zh", bool Refresh = false);
public sealed record ArtistBiography(string Title, string Text, string Language, string Url, string? WikidataId = null);
public sealed record ArtistFact(string Label, string Value);
public sealed record ArtistLink(string Label, string Url);
public sealed record ArtistOnlineRelation(string Name, string Role, string Url);
public sealed record ArtistEvent(string Name, string Date, string? Venue, string Url);
public sealed record ArtistInformation(string Status, DateTimeOffset FetchedAt, ArtistBiography? Biography,
    IReadOnlyList<ArtistFact> Facts, IReadOnlyList<ArtistOnlineRelation> Relations,
    IReadOnlyList<ArtistEvent> Events, IReadOnlyList<ArtistLink> Links, IReadOnlyList<string> Errors, string EventStatus = "unavailable");
public interface IArtistInformationClient
{
    Task<ArtistInformation> GetArtistInformationAsync(ArtistInformationRequest request, CancellationToken cancellation = default);
}
