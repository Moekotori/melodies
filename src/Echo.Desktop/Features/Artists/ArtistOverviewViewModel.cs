using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Artists;

public sealed partial class ArtistOverviewViewModel : ObservableObject, IDisposable
{
    private readonly ArtistRow artist;
    private readonly IArtistLibrary library;
    private readonly LocalizationService text;
    public ArtistOverviewViewModel(ArtistRow artist, IArtistLibrary library, LocalizationService? localization = null)
    {
        this.artist = artist; this.library = library; text = localization ?? new();
        text.Changed += OnLanguage;
    }
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? request;
    private bool attemptedOnline;
    [ObservableProperty] private ArtistInsights? insights;
    [ObservableProperty] private ArtistInformation? information;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isOnlineLoading;
    [ObservableProperty] private bool biographyExpanded;
    [ObservableProperty] private string localError = "";
    [ObservableProperty] private string onlineNotice = "";
    public string Name => artist.Name;
    public string Biography => Information?.Biography?.Text ?? text.Translate(IsOnlineLoading ? "正在读取艺人介绍…" : "尚未找到可确认的艺人介绍。");
    public string Attribution => Information?.Biography is { } bio ? text.Format("artist.detail.format.attribution", bio.Language) : text["artist.detail.independent"];
    public double BiographyHeight => BiographyExpanded ? double.PositiveInfinity : 176;
    public bool HasBiography => Information?.Biography is not null;
    public string BiographyAction => text.Translate(BiographyExpanded ? "收起介绍" : "展开介绍");
    public string FavoriteText => Insights is { } data ? $"{data.FavoriteTracks:N0}" : "—";
    public string Years => Insights?.FirstYear is { } first ? Insights.LastYear is { } last && last != first ? $"{first} — {last}" : first.ToString() : "—";
    public string Plays => Insights is { } data ? data.Plays.ToString("N0") : "—";
    public string Listened => Insights is { } data ? FormatDuration(data.ListenedSeconds, text) : "—";
    public string LastPlayed => Insights?.LastPlayedUnix is { } time ? DateTimeOffset.FromUnixTimeSeconds(time).ToLocalTime().ToString("yyyy.MM.dd") : text["artist.detail.noPlays"];
    public string ListeningStatus => IsLoading ? text["artist.detail.loadingHistory"] : Insights is { Plays: > 0 } ? text.Format("artist.detail.format.last", LastPlayed) : text["artist.detail.historyHint"];
    public string DiscoverLabel => text.Format("artist.detail.format.discover", Insights?.UnplayedAlbums ?? 0);
    public string Updated => Information is { } data ? text.Format("artist.detail.format.updated", data.FetchedAt.ToLocalTime().ToString("yyyy.MM.dd HH:mm")) : "";
    public string EventsStatus => text.Translate(IsOnlineLoading ? "正在查询活动…" : Information?.EventStatus is not "ready"
        ? "尚未取得可确认的活动资料，可查看来源或稍后刷新。" : "MusicBrainz 暂无已确认的近期活动。");
    public IReadOnlyList<ArtistHighlight> TopTracks => Insights?.TopTracks.Select(t => new ArtistHighlight(t.Track, text.Format("artist.detail.format.trackPlays", t.Plays, FormatDuration(t.Seconds, text)))).ToArray() ?? [];
    public IReadOnlyList<ArtistFact> Facts => Information?.Facts.Select(fact => new ArtistFact(text.Translate(fact.Label), text.Translate(fact.Value))).ToArray() ?? [];
    public IReadOnlyList<ArtistOnlineRelation> Relations => Information?.Relations.Select(relation => relation with { Role = text.Translate(relation.Role) }).ToArray() ?? [];
    public string OnlineNoticeText => string.Join('\n', OnlineNotice.Split('\n').Select(text.Translate));
    public string LocalErrorText => text.Translate(LocalError);
    partial void OnOnlineNoticeChanged(string value) => OnPropertyChanged(nameof(OnlineNoticeText));
    partial void OnLocalErrorChanged(string value) => OnPropertyChanged(nameof(LocalErrorText));
    public IReadOnlyList<ArtistHighlight> Collaborations => Insights?.Collaborations.Select(t => new ArtistHighlight(t, t.Artist)).ToArray() ?? [];
    public bool HasTopTracks => Insights?.TopTracks.Count > 0;
    public bool HasRelations => Insights?.RelatedArtists.Count > 0;
    public bool HasCollaborations => Insights?.Collaborations.Count > 0;
    public bool HasDiscover => Insights?.DiscoverAlbums.Count > 0;
    public bool HasFacts => Information?.Facts.Count > 0;
    public bool HasOnlineRelations => Information?.Relations.Count > 0;
    public bool HasEvents => Information?.Events.Count > 0;
    partial void OnBiographyExpandedChanged(bool value) { OnPropertyChanged(nameof(BiographyHeight)); OnPropertyChanged(nameof(BiographyAction)); }
    partial void OnInsightsChanged(ArtistInsights? value) => NotifyLocal();
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ListeningStatus));
    partial void OnIsOnlineLoadingChanged(bool value) { OnPropertyChanged(nameof(Biography)); OnPropertyChanged(nameof(EventsStatus)); }
    partial void OnInformationChanged(ArtistInformation? value)
    {
        foreach (var name in new[] { nameof(Biography), nameof(Attribution), nameof(HasBiography), nameof(HasFacts), nameof(HasOnlineRelations), nameof(HasEvents), nameof(Updated), nameof(EventsStatus), nameof(Facts), nameof(Relations) }) OnPropertyChanged(name);
    }
    private void NotifyLocal()
    {
        foreach (var name in new[] { nameof(FavoriteText), nameof(Years), nameof(Plays), nameof(Listened), nameof(LastPlayed), nameof(ListeningStatus), nameof(DiscoverLabel), nameof(TopTracks), nameof(Collaborations), nameof(HasTopTracks), nameof(HasRelations), nameof(HasCollaborations), nameof(HasDiscover) }) OnPropertyChanged(name);
    }
    public void ApplyFavoriteChange(bool favorite)
    { if (Insights is { } current) Insights = current with { FavoriteTracks = Math.Clamp(current.FavoriteTracks + (favorite ? 1 : -1), 0, artist.TrackCount) }; }
    public Task LoadAsync() => RefreshAsync();
    [RelayCommand] private void ToggleBiography() => BiographyExpanded = !BiographyExpanded;
    [RelayCommand] private async Task RefreshAsync()
    {
        if (lifetime.IsCancellationRequested || IsLoading || IsOnlineLoading) return;
        using var current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); request = current;
        await Task.WhenAll(LoadLocalAsync(current.Token), LoadOnlineAsync(current.Token));
        if (ReferenceEquals(request, current)) request = null;
    }
    private async Task LoadLocalAsync(CancellationToken token)
    {
        IsLoading = true; LocalError = "";
        try { var result = await library.GetArtistInsightsAsync(artist.Id, artist.Grouping, token); if (!token.IsCancellationRequested) Insights = result; }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!token.IsCancellationRequested) LocalError = "曲库统计读取失败，请刷新重试。"; }
        finally { if (!token.IsCancellationRequested) IsLoading = false; }
    }
    private async Task LoadOnlineAsync(CancellationToken token)
    {
        if (library is not IArtistInformationClient client) { OnlineNotice = "当前连接未提供艺人在线资料。"; return; }
        IsOnlineLoading = true; OnlineNotice = "";
        try
        {
            var refresh = attemptedOnline; attemptedOnline = true;
            var language = text.Current.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
            var result = await client.GetArtistInformationAsync(new(artist.Name, language, refresh), token);
            if (token.IsCancellationRequested) return;
            Information = result; OnlineNotice = string.Join("\n", result.Errors);
        }
        catch (OperationCanceledException) { if (!token.IsCancellationRequested) OnlineNotice = "资料查询超时，可刷新重试。"; }
        catch (Exception) { if (!token.IsCancellationRequested) OnlineNotice = "艺人在线资料暂时无法连接，可刷新重试。"; }
        finally { if (!token.IsCancellationRequested) IsOnlineLoading = false; }
    }
    private void OnLanguage()
    {
        NotifyLocal(); OnInformationChanged(Information); OnBiographyExpandedChanged(BiographyExpanded);
        OnPropertyChanged(nameof(OnlineNoticeText)); OnPropertyChanged(nameof(LocalErrorText));
    }
    public static string FormatDuration(double seconds, LocalizationService? text = null) => text is not null
        ? text.Format(seconds >= 3600 ? "artist.detail.format.hours" : "artist.detail.format.minutes", seconds >= 3600 ? seconds / 3600 : Math.Max(0, seconds) / 60)
        : seconds >= 3600 ? $"{seconds / 3600:0.#} 小时" : $"{Math.Max(0, seconds) / 60:0} 分钟";
    public void Dispose() { text.Changed -= OnLanguage; lifetime.Cancel(); request?.Cancel(); Insights = null; Information = null; }
}
public sealed record ArtistHighlight(TrackRow Track, string Detail);
