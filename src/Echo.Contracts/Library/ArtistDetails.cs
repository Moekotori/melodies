namespace Echo.Contracts.Library;

public sealed record ArtistInsights(long Revision, int FavoriteTracks, int? FirstYear, int? LastYear,
    long Plays, double ListenedSeconds, long? LastPlayedUnix, int UnplayedAlbums,
    IReadOnlyList<ListeningTrack> TopTracks, IReadOnlyList<ArtistRelation> RelatedArtists,
    IReadOnlyList<TrackRow> Collaborations, IReadOnlyList<AlbumRow> DiscoverAlbums, ArtistCollectionFacts? Collection = null,
    IReadOnlyList<ArtistCollaborationCredit>? CollaborationCredits = null);
public sealed record ArtistCollaborationCredit(string TrackId, IReadOnlyList<string> Artists);
public sealed record ArtistCollectionFacts(IReadOnlyList<string> Genres, int LosslessCount, int CodecTotal,
    string? TopSpec, int PrimaryAlbums, int AppearanceTracks);
public sealed record ArtistRelation(ArtistRow Artist, int SharedTracks, string Kind = "collaboration");
public sealed record ArtistInformationRequest(string Name, string Language = "zh", bool Refresh = false, bool IncludeFacts = false);
public sealed record ArtistBiography(string Title, string Text, string Language, string Url, string? WikidataId = null, string? Description = null,
    string? ThumbnailUrl = null, string? ArtworkReference = null);
public sealed record ArtistFact(string Label, string Value);
public sealed record ArtistLink(string Label, string Url);
public sealed record ArtistOnlineRelation(string Name, string Role, string Url, ArtistRow? LocalArtist = null);
public sealed record ArtistEvent(string Name, string Date, string? Venue, string Url, string Source = "MusicBrainz",
    string? Time = null, string? Region = null, string? Country = null, string? TicketUrl = null, string? Timezone = null);
public sealed record ArtistInformation(string Status, DateTimeOffset FetchedAt, ArtistBiography? Biography,
    IReadOnlyList<ArtistFact> Facts, IReadOnlyList<ArtistOnlineRelation> Relations,
    IReadOnlyList<ArtistEvent> Events, IReadOnlyList<ArtistLink> Links, IReadOnlyList<string> Errors, string EventStatus = "unavailable", ArtistPublicFacts? PublicFacts = null,
    IReadOnlyList<string>? EventFailures = null);
public sealed record ArtistPublicFacts(string Name, string Url, string Match, IReadOnlyList<string> Aliases,
    IReadOnlyList<ArtistFact> Facts, IReadOnlyList<ArtistOnlineRelation> Members, string? Description = null);
public interface IArtistInformationClient
{
    Task<ArtistInformation> GetArtistInformationAsync(ArtistInformationRequest request, CancellationToken cancellation = default);
}
