using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Application.Podcast;
using Echo.Contracts.Podcast;
using Echo.Desktop.Shell;
using Echo.Playback;

namespace Echo.Desktop.Features.Podcasts;

public sealed partial class PodcastViewModel : ObservableObject, IDisposable
{
    private readonly IPodcastLibrary? library;
    private readonly PodcastPlayback? playback;
    private readonly IPlaybackSession? session;
    private readonly Echo.Desktop.Features.Artwork.RemoteCoverLoader covers = new(
        maxBytes: 2 * 1024 * 1024, maxPixels: 4_000_000, edge: 256, timeoutSeconds: 8, automaticRedirects: false);
    private readonly List<PodcastShowRow> all = [];
    private readonly CancellationTokenSource lifetime = new();
    private int episodeGeneration;
    private bool disposed;
    private string? currentId;
    private bool currentPlaying;

    public ObservableCollection<PodcastShowRow> Shows { get; } = [];
    public ObservableCollection<PodcastEpisodeRow> Episodes { get; } = [];
    [ObservableProperty] private PodcastShowRow? selected;
    [ObservableProperty] private string showQuery = "";
    [ObservableProperty] private string episodeQuery = "";
    [ObservableProperty] private int filterIndex;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool editorOpen;
    [ObservableProperty] private bool confirmRemove;
    [ObservableProperty] private string draftUrl = "";
    [ObservableProperty] private bool descriptionOpen;
    [ObservableProperty] private Bitmap? cover;
    [ObservableProperty] private int page;
    [ObservableProperty] private int total;
    [ObservableProperty] private bool listFailed;
    [ObservableProperty] private bool episodesFailed;

    public string ShowTitle => Selected?.Feed.Title ?? "播客";
    public string ShowAuthor => Selected?.Feed.Author ?? "";
    public string ShowDescription => Selected?.Feed.Description ?? "";
    public int DescriptionLines => DescriptionOpen ? 12 : 3;
    public string DescriptionToggle => DescriptionOpen ? "收起简介" : "展开简介";
    public bool HasShows => Shows.Count > 0;
    public bool HasEpisodes => Episodes.Count > 0;
    public string CountText => $"{Total} 集";
    public string PageText => Total == 0 ? "" : $"第 {Page + 1} 页";
    public bool HasPrevious => Page > 0;
    public bool HasNext => (Page + 1) * PodcastLimits.PageSize < Total;
    public string EmptyTitle => library is null ? "播客还没有准备好" : "从一个喜欢的节目开始";
    public string EmptyDescription => library is null ? "订阅数据暂时不可用。" : "粘贴节目公开的 RSS 地址，订阅后即可浏览和收听单集。";

    public PodcastViewModel(ShellViewModel shell)
    {
        library = shell.Podcasts;
        playback = shell.PodcastPlayback;
        session = shell.Player?.Session;
        covers.Changed += CoverChanged;
        if (session is not null) session.Changed += PlaybackChanged;
    }

    public async Task LoadAsync()
    {
        if (library is null || disposed) return;
        try
        {
            var feeds = await library.ListAsync(lifetime.Token).ConfigureAwait(true);
            if (disposed) return;
            all.Clear();
            all.AddRange(feeds.Select(feed => new PodcastShowRow(feed)));
            ListFailed = false;
            ApplyShowFilter();
            var playing = session?.Snapshot.Current;
            var preferred = playing?.Origin?.Kind == "podcast" ? playing.Origin.Id : null;
            Selected = all.FirstOrDefault(row => row.Id == preferred) ?? all.FirstOrDefault();
            MarkSelected();
            await PublishShow().ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException) { ListFailed = true; Message = Explain(error); }
    }

    [RelayCommand] private void ToggleEditor() { EditorOpen = !EditorOpen; ConfirmRemove = false; if (!EditorOpen) DraftUrl = ""; }
    [RelayCommand] private void ToggleDescription() { DescriptionOpen = !DescriptionOpen; OnPropertyChanged(nameof(DescriptionLines)); OnPropertyChanged(nameof(DescriptionToggle)); }
    [RelayCommand] private void Select(PodcastShowRow? row)
    {
        if (row is null || row == Selected) return;
        Selected = row; MarkSelected(); Page = 0; EpisodeQuery = ""; FilterIndex = 0; DescriptionOpen = false; ConfirmRemove = false;
        OnPropertyChanged(nameof(DescriptionLines));
        _ = PublishShow();
    }
    [RelayCommand] private async Task SubscribeAsync()
    {
        if (library is null || Busy) return;
        Busy = true; Message = "";
        try
        {
            var feed = await library.SubscribeAsync(DraftUrl, lifetime.Token).ConfigureAwait(true);
            if (disposed) return;
            var row = all.FirstOrDefault(item => item.Id == feed.Id);
            if (row is null) { row = new(feed); all.Add(row); }
            else row.Replace(feed);
            ShowQuery = ""; DraftUrl = ""; EditorOpen = false; ListFailed = false;
            ApplyShowFilter();
            Selected = all.First(item => item.Id == feed.Id);
            MarkSelected(); Page = 0;
            await PublishShow().ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException) { Message = Explain(error); }
        finally { Busy = false; }
    }
    [RelayCommand] private async Task RefreshAsync()
    {
        if (library is null || Selected is not { } row || Busy) return;
        Busy = true; Message = "";
        try
        {
            var feed = await library.RefreshAsync(row.Id, lifetime.Token).ConfigureAwait(true);
            if (disposed || Selected?.Id != row.Id) return;
            row.Replace(feed); covers.Show(feed.CoverUrl); EpisodesFailed = false;
            await LoadEpisodesAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException) { Message = Explain(error); }
        finally { Busy = false; }
    }
    [RelayCommand] private void AskRemove() => ConfirmRemove = Selected is not null;
    [RelayCommand] private void CancelRemove() => ConfirmRemove = false;
    [RelayCommand] private async Task RemoveAsync()
    {
        if (library is null || Selected is not { } row) return;
        Busy = true;
        try
        {
            await library.UnsubscribeAsync(row.Id, lifetime.Token).ConfigureAwait(true);
            if (disposed) return;
            all.RemoveAll(item => item.Id == row.Id);
            ConfirmRemove = false;
            ApplyShowFilter();
            Selected = all.FirstOrDefault();
            MarkSelected();
            await PublishShow().ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException) { Message = Explain(error); }
        finally { Busy = false; }
    }
    [RelayCommand] private Task RetryListAsync() { ListFailed = false; Message = ""; return LoadAsync(); }

    partial void OnShowQueryChanged(string value) => ApplyShowFilter();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (session is not null) session.Changed -= PlaybackChanged;
        covers.Changed -= CoverChanged;
        lifetime.Cancel();
        covers.Dispose();
        lifetime.Dispose();
    }

    private void ApplyShowFilter()
    {
        var query = ShowQuery.Trim();
        IEnumerable<PodcastShowRow> visible = string.IsNullOrEmpty(query)
            ? all
            : all.Where(row => $"{row.Title} {row.Author}".Contains(query, StringComparison.OrdinalIgnoreCase));
        Shows.Clear();
        foreach (var row in visible) Shows.Add(row);
        MarkSelected();
        OnPropertyChanged(nameof(HasShows));
    }

    private void MarkSelected()
    {
        foreach (var row in all) row.IsSelected = row == Selected;
    }

    private Task PublishShow()
    {
        OnPropertyChanged(nameof(ShowTitle));
        OnPropertyChanged(nameof(ShowAuthor));
        OnPropertyChanged(nameof(ShowDescription));
        OnPropertyChanged(nameof(DescriptionLines));
        OnPropertyChanged(nameof(HasShows));
        covers.Show(Selected?.Feed.CoverUrl);
        return LoadEpisodesAsync();
    }

    private void CoverChanged() { if (!disposed) Cover = covers.Image; }

    private void PlaybackChanged(PlaybackSnapshot snapshot)
    {
        var podcast = snapshot.Current?.Origin?.Kind == "podcast";
        var id = podcast ? snapshot.Current?.ItemId : null;
        var playing = podcast && snapshot.Status is PlaybackStatus.Playing or PlaybackStatus.Loading;
        if (id == currentId && playing == currentPlaying) return;
        currentId = id; currentPlaying = playing;
        Dispatcher.UIThread.Post(() => { if (!disposed) foreach (var row in Episodes) row.Update(currentId, currentPlaying); });
    }

    private static string Explain(Exception error) => error is PodcastFailure failure ? failure.Code switch
    {
        "invalid_url" => "请输入有效的 HTTP 或 HTTPS RSS 地址。",
        "feed_too_large" => "RSS 超过 2 MB，请使用较小的订阅源。",
        "invalid_feed" or "no_audio" => "这个地址没有有效的 RSS 音频单集，请检查节目订阅地址。",
        "feed_limit" => "最多订阅 100 个节目，请先取消一个订阅。",
        "busy" => "正在读取另一个节目，请稍后再试。",
        "not_found" => "这个节目已经不在订阅里。",
        "storage_failed" => "无法保存或读取播客数据，请重试。",
        _ => "操作失败，请检查网络连接后重试。"
    } : "操作失败，请检查网络连接后重试。";
}
