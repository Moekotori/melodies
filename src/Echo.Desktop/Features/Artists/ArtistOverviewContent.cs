using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Artists;

public sealed partial class ArtistOverviewViewModel
{
    [ObservableProperty] private ArtistPublicFacts? publicFacts;
    [ObservableProperty] private bool factsAttempted;
    [ObservableProperty] private bool isFactsLoading;
    [ObservableProperty] private string factsError = "";
    [ObservableProperty] private string? playingTrackId;
    partial void OnPlayingTrackIdChanged(string? value) => OnPropertyChanged(nameof(Collaborations));
    private IReadOnlyList<ArtistLink> sourceLinks = [];
    private IReadOnlyList<ArtistFact> libraryFacts = [];
    private CancellationTokenSource? factsRequest;
    private int factsGeneration;
    public IReadOnlyList<ArtistLink> SourceLinks => sourceLinks;
    public IReadOnlyList<ArtistLink> MetadataLinks => sourceLinks.Where(link => !ArtistSourceLinks.IsSocial(link)).ToArray();
    public IReadOnlyList<ArtistSocialAccount> SocialAccounts { get; private set; } = [];
    public bool HasSocialAccounts => SocialAccounts.Count > 0;
    public IReadOnlyList<ArtistFact> LibraryFacts => libraryFacts;
    public IReadOnlyList<string> Genres => Insights?.Collection?.Genres ?? [];
    public bool HasGenres => Genres.Count > 0;
    public bool HasListening => Insights is { Plays: > 0 };
    public string MembersSummary => string.Join(" / ", Information?.Relations.Where(relation => relation.Role is "成员" or "所属组合")
        .Select(relation => relation.Name + " · " + text[relation.LocalArtist is null ? "artist.content.memberMissing" : "artist.content.memberLocal"]) ?? []);
    public string CareerSummary
    {
        get
        {
            var from = PublicFacts?.Facts.FirstOrDefault(fact => fact.Label == "活动开始")?.Value ?? Information?.Facts.FirstOrDefault(fact => fact.Label == "成立日期")?.Value;
            var to = PublicFacts?.Facts.FirstOrDefault(fact => fact.Label == "活动结束")?.Value ?? Information?.Facts.FirstOrDefault(fact => fact.Label == "结束日期")?.Value;
            return from is null || Insights?.FirstYear is null ? "" : text.Format("artist.content.career", string.Join("–", new[] { from, to }.OfType<string>()), Years);
        }
    }
    public string RoleSummary => Insights?.Collection is { } collection ? text.Format("artist.content.primary", collection.PrimaryAlbums)
        + (collection.AppearanceTracks > 0 ? " · " + text.Format("artist.content.appearances", collection.AppearanceTracks) : "") : "";
    public string FactsAction => text[IsFactsLoading ? "artist.content.factsLoading" : FactsAttempted ? "artist.content.refreshFacts" : "artist.content.loadFacts"];
    public bool CanLoadFacts => !IsOnlineLoading && !IsFactsLoading;
    public bool NoPublicFacts => FactsAttempted && PublicFacts is null && FactsError.Length == 0;
    public string FactsMatch => PublicFacts is { } data ? text[data.Match == "musicbrainz-id" ? "artist.content.matchedId" : "artist.content.matchedWiki"] + " · " + data.Name : "";
    public string Aliases => PublicFacts is { } data ? string.Join(" / ", data.Aliases) : "";
    public IReadOnlyList<ArtistFact> PublicFactRows => PublicFacts?.Facts.Where(fact => fact.Label is "国籍" or "国家 / 来源地" or "活动开始" or "活动结束" or "成立" or "解散")
        .Select(fact => new ArtistFact(text.Translate(fact.Label), fact.Value)).ToArray() ?? [];
    public IReadOnlyList<ArtistOnlineRelation> PublicMembers => PublicFacts?.Members.Select(member => member with { Role = TranslateRole(member.Role) }).ToArray() ?? [];
    private string TranslateRole(string role) => string.Join(" · ", role.Split(" · ").Select(text.Translate));
    public IReadOnlyList<ArtistRelatedItem> RelatedArtists => Insights?.RelatedArtists.Select(relation => new ArtistRelatedItem(relation.Artist,
        text[relation.Kind == "sameAlbum" ? "artist.content.sameAlbum" : "artist.content.collaboration"] + " · " + text.Format("artist.content.trackCount", relation.Artist.TrackCount))).ToArray() ?? [];
    private void RefreshLibraryFacts()
    { libraryFacts = ArtistOverviewPresentation.LibraryFacts(artist, Insights, ArtistSourceLinks.Providers(sourceLinks), text); OnPropertyChanged(nameof(LibraryFacts)); }
    private void NotifyContent()
    {
        RefreshLibraryFacts();
        foreach (var name in new[] { nameof(Genres), nameof(HasGenres), nameof(RoleSummary), nameof(HasListening), nameof(RelatedArtists), nameof(CareerSummary) }) OnPropertyChanged(name);
    }
    partial void OnIsFactsLoadingChanged(bool value) { OnPropertyChanged(nameof(FactsAction)); OnPropertyChanged(nameof(CanLoadFacts)); }
    partial void OnFactsAttemptedChanged(bool value) { OnPropertyChanged(nameof(FactsAction)); OnPropertyChanged(nameof(NoPublicFacts)); }
    partial void OnFactsErrorChanged(string value) => OnPropertyChanged(nameof(NoPublicFacts));
    partial void OnPublicFactsChanged(ArtistPublicFacts? value)
    { foreach (var name in new[] { nameof(FactsMatch), nameof(Aliases), nameof(PublicFactRows), nameof(PublicMembers), nameof(NoPublicFacts), nameof(CareerSummary) }) OnPropertyChanged(name); }
    [RelayCommand] private async Task LoadFactsAsync()
    {
        if (!CanLoadFacts || lifetime.IsCancellationRequested || library is not IArtistInformationClient client) return;
        using var current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        factsRequest = current; var generation = ++factsGeneration;
        IsFactsLoading = true; FactsError = "";
        try
        {
            var language = text.Current.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
            var result = await client.GetArtistInformationAsync(new(artist.Name, language, FactsAttempted, IncludeFacts: true), current.Token);
            if (current.IsCancellationRequested || generation != factsGeneration) return;
            PublicFacts = result.PublicFacts; FactsAttempted = true;
            if (result.Errors.Count > 0) FactsError = text[PublicFacts is not null ? "artist.content.factsStale" : "artist.content.factsUnavailable"];
        }
        catch (Exception error) when (error is not OperationCanceledException) { if (!current.IsCancellationRequested && generation == factsGeneration) FactsError = text["artist.content.factsUnavailable"]; }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(factsRequest, current)) factsRequest = null;
            if (!lifetime.IsCancellationRequested && generation == factsGeneration) IsFactsLoading = false;
        }
    }
    private void ResetPublicFacts()
    {
        factsGeneration++; factsRequest?.Cancel(); factsRequest = null;
        PublicFacts = null; FactsAttempted = false; FactsError = ""; IsFactsLoading = false;
    }
}
public sealed record ArtistRelatedItem(ArtistRow Artist, string Detail)
{
    public string Monogram => ArtistMonogram.Wall(Artist.Name);
}
