using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Albums;
using Echo.Playback;
using Echo.Desktop.Localization;
using Echo.Infrastructure.Playback;

namespace Echo.Desktop.Features.Queue;

// Window-owned, bounded view of the session. Position notifications never rebuild queue rows.
public sealed partial class QueueViewModel : ObservableObject, IDisposable
{
    private readonly LocalizationService Text;
    private readonly PlaybackSessionCoordinator session;
    private readonly CancellationTokenSource lifetime = new();
    private readonly PlaybackStateSubscription subscription;
    private QueueSnapshot state;
    private QueueSnapshot? pending;
    private int updateQueued;
    private string[] selection = [];
    private bool disposed;
    private bool presentationActive = true;
    [ObservableProperty] private IReadOnlyList<QueueRowViewModel> rows = [];
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool showHistory;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private QueueTrack? upcoming;
    [ObservableProperty] private AlbumArtworkCache? artwork;
    partial void OnArtworkChanged(AlbumArtworkCache? value) => RefreshRows();
    public string Heading => ShowHistory ? Text.Translate("播放历史") : Text.Translate("队列");
    public string SectionTitle => ShowHistory ? Text.Translate("最近播放") : BaseAlbum() is not null ? Text.Translate("专辑曲目") : Text.Translate("接下来播放");
    public string CountText => Text.Format("queue.count", state.Items.Count);
    public string Summary => CountText + DurationSummary(state.Items);
    public string CurrentSource => session.Snapshot.Current?.Origin?.Label ?? Text.Translate("手动添加");
    public string CurrentQuality => session.Snapshot.Current is { } track ? new QueueRowViewModel(track, 0, null).Quality : "—";
    public string PlayLabel => Text.Translate("播放");
    public string ManageLabel => Text.Translate("队列管理");
    public string SourceHeading => Text.Translate("来源");
    public string CurrentArtworkPath => session.Snapshot.Current?.Metadata?.GetValueOrDefault("coverPath") ?? session.Snapshot.Current?.FilePath ?? "";
    public string SelectionText => Text.Format("queue.count", selection.Length);
    public string ResultText => Text.Format("queue.count", Rows.Count);
    public bool IsEmpty => Rows.Count == 0;
    public string EmptyTitle => Search.Length > 0 ? Text.Translate("没有找到匹配的曲目") : ShowHistory ? Text.Translate("还没有播放历史") : Text.Translate("队列是空的");
    public string EmptyHint => Search.Length > 0 ? Text.Translate("试试曲名、艺术家或专辑。") : ShowHistory ? Text.Translate("切换曲目后，已播放的歌曲会出现在这里。") : Text.Translate("打开音频文件，或从专辑添加歌曲。");
    public bool HasSelection => selection.Length > 0 && !ShowHistory;
    public bool CanUndo => session.CanUndoQueueEdit;
    public bool IsSequential => state.Mode == QueuePlaybackMode.Sequential;
    public bool IsShuffle => state.Mode == QueuePlaybackMode.Shuffle;
    public bool IsRepeatOne => state.Mode == QueuePlaybackMode.RepeatOne;
    public bool IsRepeatAll => state.Mode == QueuePlaybackMode.RepeatAll;
    public string UpcomingTitle => Upcoming is null ? IsRepeatOne ? Text.Translate("单曲循环") : Text.Translate("没有下一首") : new QueueRowViewModel(Upcoming, 0, null).Title;
    public string UpcomingArtist => Upcoming?.Metadata?.GetValueOrDefault("artist") ?? "";
    public string UpcomingPath => Upcoming?.Metadata?.GetValueOrDefault("coverPath") ?? Upcoming?.FilePath ?? "";
    public bool HasUpcoming => Upcoming is not null;
    public bool CanPlay => session.Snapshot.Current is not null || state.Items.Count > 0;
    public long Version => state.Version;
    public QueueViewModel(PlaybackSessionCoordinator session, LocalizationService? text = null)
    {
        Text = text ?? new(); Text.Changed += RefreshLanguage;
        this.session = session;
        PlaybackStateSubscription? acquired = null;
        try
        {
            subscription = acquired = session.Subscribe(PlaybackStateChanges.Queue);
            state = subscription.Initial.Queue;
            Apply(state);
        }
        catch { acquired?.Dispose(); Text.Changed -= RefreshLanguage; lifetime.Dispose(); throw; }
        _ = Task.Run(ReadStatesAsync);
    }
    private void RefreshLanguage()
    {
        RefreshDrawerRows();
        foreach (var name in new[] { nameof(Heading), nameof(SectionTitle), nameof(CountText), nameof(ResultText),
            nameof(EmptyTitle), nameof(EmptyHint), nameof(UpcomingTitle), nameof(Summary), nameof(ManageLabel), nameof(PlayLabel), nameof(SourceHeading) }) OnPropertyChanged(name);
    }
    private async Task ReadStatesAsync()
    {
        var token = lifetime.Token;
        try { while (true) OnQueueChanged((await subscription.ReadAsync(token).ConfigureAwait(false)).State.Queue); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (System.Threading.Channels.ChannelClosedException) { }
    }
    private void OnQueueChanged(QueueSnapshot value)
    {
        Interlocked.Exchange(ref pending, value);
        if (Interlocked.Exchange(ref updateQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref updateQueued, 0);
            var latest = Interlocked.Exchange(ref pending, null);
            if (!disposed && latest is not null) Apply(latest);
        });
    }
    private void Apply(QueueSnapshot value)
    {
        state = value; Upcoming = state.Upcoming;
        RefreshRows();
        foreach (var name in new[] { nameof(CountText), nameof(Summary), nameof(CurrentSource), nameof(CurrentQuality), nameof(CurrentArtworkPath), nameof(CanUndo), nameof(IsSequential), nameof(IsShuffle), nameof(IsRepeatOne),
            nameof(IsRepeatAll), nameof(UpcomingTitle), nameof(UpcomingArtist), nameof(UpcomingPath), nameof(HasUpcoming) }) OnPropertyChanged(name);
    }
    private QueueOrigin? BaseAlbum()
    {
        var index = state.Items.ToList().FindIndex(x => x.ItemId == state.CurrentItemId);
        var current = index < 0 ? null : state.Items[index];
        var origin = current is { QueuedNext: false } ? current.Origin
            : state.Items.Take(Math.Max(0, index)).LastOrDefault(x => !x.QueuedNext)?.Origin;
        return origin?.Kind == "album" ? origin : null;
    }
    private void RefreshRows()
    {
        if (!presentationActive) { Rows = []; RefreshDrawerRows(); return; }
        var album = BaseAlbum();
        var index = state.Items.ToList().FindIndex(x => x.ItemId == state.CurrentItemId);
        var source = ShowHistory ? state.History.Reverse().ToArray() : state.Items;
        Rows = source.Select((track, i) => new QueueRowViewModel(track, i, state.CurrentItemId, !ShowHistory && i < index, Artwork))
            .Where(row => ShowHistory || index < 0 || row.Index >= index || (!row.Track.QueuedNext && row.Track.Origin?.Id == album?.Id && album is not null))
            .Where(row => Search.Length == 0 || new[] { row.Title, row.Artist, row.Album, row.Source }.Any(value => value.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        RefreshDrawerRows();
        foreach (var name in new[] { nameof(Heading), nameof(SectionTitle), nameof(ResultText), nameof(IsEmpty), nameof(EmptyTitle), nameof(EmptyHint), nameof(HasSelection) }) OnPropertyChanged(name);
    }
    partial void OnSearchChanged(string value) => RefreshRows();
    public void SetPresentationActive(bool active)
    {
        if (disposed || presentationActive == active) return;
        presentationActive = active;
        if (!active) selection = [];
        RefreshRows();
    }
    partial void OnShowHistoryChanged(bool value) { selection = []; RefreshRows(); }
    public void SetSelection(IEnumerable<QueueRowViewModel> rows)
    { selection = rows.Select(x => x.ItemId).Distinct().Take(1000).ToArray(); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(SelectionText)); }
    public IReadOnlyList<string> Selection => selection;
    [RelayCommand] private Task SequentialAsync() => RunAsync(() => session.SetQueueModeAsync(QueuePlaybackMode.Sequential, lifetime.Token));
    [RelayCommand] private Task ShuffleAsync() => RunAsync(() => session.SetQueueModeAsync(QueuePlaybackMode.Shuffle, lifetime.Token));
    [RelayCommand] private Task RepeatOneAsync() => RunAsync(() => session.SetQueueModeAsync(QueuePlaybackMode.RepeatOne, lifetime.Token));
    [RelayCommand] private Task RepeatAllAsync() => RunAsync(() => session.SetQueueModeAsync(QueuePlaybackMode.RepeatAll, lifetime.Token));
    [RelayCommand] private Task ClearAsync() => RunAsync(() => session.ClearQueueAsync(lifetime.Token), Text.Translate("已清空队列，当前歌曲继续播放。"));
    [RelayCommand] private Task RemoveSelectedAsync() => RunAsync(() => session.RemoveQueueItemsAsync(selection, lifetime.Token), Text.Translate("已移除所选曲目。"));
    [RelayCommand] private Task MoveNextAsync() => RunAsync(() => session.MoveQueueItemsNextAsync(selection, lifetime.Token), Text.Translate("已加入下一首播放。"));
    [RelayCommand] private Task RemoveAfterPlayAsync() => RunAsync(() => session.SetRemoveAfterPlayAsync(selection, true, lifetime.Token), Text.Translate("所选曲目将在播放后移除。"));
    [RelayCommand] private Task KeepAfterPlayAsync() => RunAsync(() => session.SetRemoveAfterPlayAsync(selection, false, lifetime.Token));
    [RelayCommand] private Task UndoAsync() => RunAsync(() => session.UndoQueueEditAsync(lifetime.Token), Text.Translate("已撤销队列编辑。"));
    [RelayCommand] private Task PlayUpcomingAsync() => Upcoming is null ? Task.CompletedTask : RunAsync(() => session.SelectQueueItemAsync(Upcoming.ItemId, lifetime.Token));
    private string DurationSummary(IReadOnlyList<QueueTrack> tracks)
    {
        var seconds = tracks.Sum(track => double.TryParse(track.Metadata?.GetValueOrDefault("duration"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? Math.Clamp(value, 0, 864000) : 0);
        if (seconds <= 0) return "";
        var time = TimeSpan.FromSeconds(seconds);
        if (Text.Current.Culture.TwoLetterISOLanguageName == "zh")
            return time.TotalHours >= 1 ? $" · {(int)time.TotalHours} 小时 {time.Minutes} 分钟" : $" · {(int)time.TotalMinutes} 分钟";
        return time.TotalHours >= 1 ? $" · {(int)time.TotalHours} h {time.Minutes} min" : $" · {(int)time.TotalMinutes} min";
    }
    public Task PlayAsync(QueueRowViewModel row) => RunAsync(async () =>
    {
        if (!session.Queue.Any(x => x.ItemId == row.ItemId))
        {
            var restored = row.Track with { ItemId = Guid.NewGuid().ToString("N"), QueuedNext = true };
            await session.EnqueueAsync([restored], true, lifetime.Token);
            await session.SelectQueueItemAsync(restored.ItemId, lifetime.Token);
        }
        else await session.SelectQueueItemAsync(row.ItemId, lifetime.Token);
    });
    public Task MoveAsync(IEnumerable<string> ids, int targetIndex) => RunAsync(() => session.MoveQueueItemsAsync(ids, targetIndex, lifetime.Token));
    public Task EnqueuePathsAsync(IEnumerable<string> paths, bool nextUp = false)
        => RunAsync(async () => await session.EnqueueAsync(await PlaybackFileResolver.ResolveAsync(paths, lifetime.Token), nextUp, lifetime.Token), Text.Translate("已添加到队列。"));
    private async Task RunAsync(Func<Task> action, string success = "")
    {
        if (disposed || IsBusy) return;
        IsBusy = true; Message = "";
        try { await action(); if (!disposed) Message = success; }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) Message = Text.Format("queue.failed", error.Message); }
        finally { if (!disposed) { IsBusy = false; OnPropertyChanged(nameof(CanUndo)); } }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Text.Changed -= RefreshLanguage; lifetime.Cancel(); subscription.Dispose();
        pending = null; Rows = []; drawerRows = []; selection = [];
    }
}
