using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Core.Contracts;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Albums;

public sealed partial class AlbumOnlineViewModel(AlbumRow album, IAlbumInformationClient? client) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _request;
    private int _generation;
    private bool _loaded;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private int mode = 2;
    [ObservableProperty] private string sourceText = "尚未加载在线资料";
    [ObservableProperty] private string matchText = "";
    [ObservableProperty] private string notice = "";
    [ObservableProperty] private string updatedText = "";
    [ObservableProperty] private string emptyText = "尚未找到对应资料";
    [ObservableProperty] private bool hasContent;
    [ObservableProperty] private IReadOnlyList<OnlineFact> releaseFacts = [];
    [ObservableProperty] private IReadOnlyList<OnlineRelease> releases = [];
    [ObservableProperty] private IReadOnlyList<ContributorGroup> credits = [];
    [ObservableProperty] private IReadOnlyList<InformationLink> links = [];
    [ObservableProperty] private IReadOnlyList<OnlineArticle> articles = [];
    public string Title => album.Title;
    public string Artist => album.Artist;
    public string Overview => string.Join(" · ", new[] { album.Year?.ToString(), $"{album.TrackCount} 首歌" }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public bool IsSources => Mode == 2;
    public bool IsVersions => Mode == 3;
    public bool IsInformation => Mode == 4;
    public bool HasNotice => Notice.Length > 0;
    public bool HasMatch => MatchText.Length > 0;
    public bool HasRelease => ReleaseFacts.Count > 0;
    public bool HasCredits => Credits.Count > 0;
    public bool HasLinks => Links.Count > 0;
    public bool HasVersions => Releases.Count > 0;
    public bool HasArticles => Articles.Count > 0;
    public bool IsEmpty => !IsLoading && !(IsSources ? HasRelease || HasCredits || HasLinks : IsVersions ? HasVersions : HasArticles);
    partial void OnModeChanged(int value) { OnPropertyChanged(nameof(IsSources)); OnPropertyChanged(nameof(IsVersions)); OnPropertyChanged(nameof(IsInformation)); OnPropertyChanged(nameof(IsEmpty)); }
    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));
    partial void OnMatchTextChanged(string value) => OnPropertyChanged(nameof(HasMatch));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    public Task ShowAsync(int tab) { Mode = tab; return _loaded || IsLoading ? Task.CompletedTask : LoadAsync(false); }
    [RelayCommand] private Task RefreshAsync() => LoadAsync(true);
    private async Task LoadAsync(bool refresh)
    {
        if (_lifetime.IsCancellationRequested) return;
        _request?.Cancel(); using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _request = request;
        var generation = ++_generation;
        IsLoading = true; Notice = ""; SourceText = "正在读取在线资料…";
        try
        {
            if (client is null) throw new IOException("当前环境未连接专辑资料服务。");
            var result = await client.ReadAsync(new(new(album.Title, album.Artist, album.Year, album.TrackCount), refresh), request.Token);
            if (request.IsCancellationRequested || generation != _generation) return;
            _loaded = true;
            SourceText = result.Sources.Length > 0 ? string.Join(" / ", result.Sources) : "未找到可靠匹配";
            MatchText = result.Confidence is { } confidence ? $"{result.MatchTitle} · {result.MatchArtist} · 匹配 {confidence:P0}" + (result.Ambiguous ? " · 多个发行相近，请核对版本" : "") : "";
            Notice = string.Join("\n", result.Errors);
            if (result.Status == "stale") Notice = "当前显示离线缓存资料。\n" + Notice;
            UpdatedText = $"更新于 {result.FetchedAt.ToLocalTime():yyyy.MM.dd HH:mm}";
            ReleaseFacts = result.Release is { } release ? Facts(release) : [];
            Releases = result.Versions.Select(release => new OnlineRelease(release)).ToArray();
            Credits = result.Credits; Links = result.Links;
            Articles = new[] { result.AlbumArticle is { } article ? new OnlineArticle("关于这张专辑", article) : null,
                result.ArtistArticle is { } artist ? new OnlineArticle("关于艺术家", artist) : null }.OfType<OnlineArticle>().ToArray();
            EmptyText = result.Status == "error" ? "在线资料读取失败" : "尚未找到对应资料";
            HasContent = HasRelease || HasCredits || HasLinks || HasVersions || HasArticles;
            foreach (var name in new[] { nameof(HasRelease), nameof(HasCredits), nameof(HasLinks), nameof(HasVersions), nameof(HasArticles) }) OnPropertyChanged(name);
        }
        catch (OperationCanceledException) { if (!request.IsCancellationRequested) Notice = "资料请求超时，可点击刷新重试。"; }
        catch (Exception error) { if (!request.IsCancellationRequested) { SourceText = "资料服务暂时不可用"; Notice = error is IOException ? error.Message : "连接失败，请稍后刷新。"; EmptyText = "在线资料读取失败"; } }
        finally { if (generation == _generation) { IsLoading = false; _request = null; } }
    }
    private static OnlineFact[] Facts(AlbumRelease release) => new[]
    {
        new OnlineFact("发行标题", release.Title), new("艺术家", release.Artist), new("发行日期", release.Date), new("地区", release.Country),
        new("状态", release.Status), new("介质", string.Join(" / ", release.Formats)), new("唱片公司", string.Join(" · ", release.Labels)),
        new("目录编号", string.Join(" · ", release.CatalogNumbers)), new("条形码", release.Barcode)
    }.Where(fact => !string.IsNullOrWhiteSpace(fact.Value)).ToArray();
    public void Dispose() { _lifetime.Cancel(); _request?.Cancel(); Articles = []; Links = []; Credits = []; Releases = []; }
}
public sealed record OnlineFact(string Label, string? Value);
public sealed record OnlineArticle(string Label, InformationArticle Article)
{
    public string Attribution => $"来源：{Article.Language}.wikipedia.org · 文字采用 CC BY-SA 许可";
}
public sealed record OnlineRelease(AlbumRelease Release)
{
    public string Meta => string.Join(" · ", new[] { Release.Date, Release.Country, Release.Status, string.Join(" / ", Release.Formats) }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string Label => string.Join(" · ", Release.Labels.Concat(Release.CatalogNumbers));
    public string Badge => Release.Selected ? "匹配的发行" : "其他发行版本";
}
