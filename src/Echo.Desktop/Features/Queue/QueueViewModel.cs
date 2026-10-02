using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Albums;
using Echo.Playback;

namespace Echo.Desktop.Features.Queue;

// Window-owned, bounded view of the session. Position notifications never rebuild queue rows.
public sealed partial class QueueViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackSessionCoordinator session;
    private readonly CancellationTokenSource lifetime = new();
    private QueueSnapshot state;
    private QueueSnapshot? pending;
    private int updateQueued;
    private string[] selection = [];
    private bool disposed;
    [ObservableProperty] private IReadOnlyList<QueueRowViewModel> rows = [];
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool showHistory;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private QueueTrack? upcoming;
    public AlbumArtworkCache? Artwork { get; set; }
    public string Heading => ShowHistory ? "播放历史" : "播放队列";
    public string SectionTitle => ShowHistory ? "最近播放" : BaseAlbum() is not null ? "专辑曲目" : "接下来播放";
    public string CountText => $"{state.Items.Count:N0} 首";
    public string ResultText => $"{Rows.Count:N0} 首";
    public bool IsEmpty => Rows.Count == 0;
    public string EmptyTitle => Search.Length > 0 ? "没有找到匹配的曲目" : ShowHistory ? "还没有播放历史" : "队列是空的";
    public string EmptyHint => Search.Length > 0 ? "试试曲名、艺术家或专辑。" : ShowHistory ? "切换曲目后，已播放的歌曲会出现在这里。" : "打开音频文件，或从专辑添加歌曲。";
    public bool HasSelection => selection.Length > 0 && !ShowHistory;
    public bool CanUndo => session.CanUndoQueueEdit;
    public bool IsSequential => state.Mode == QueuePlaybackMode.Sequential;
    public bool IsShuffle => state.Mode == QueuePlaybackMode.Shuffle;
    public bool IsRepeatOne => state.Mode == QueuePlaybackMode.RepeatOne;
    public bool IsRepeatAll => state.Mode == QueuePlaybackMode.RepeatAll;
    public string UpcomingTitle => Upcoming is null ? IsRepeatOne ? "单曲循环" : "没有下一首" : new QueueRowViewModel(Upcoming, 0, null).Title;
    public string UpcomingArtist => Upcoming?.Metadata?.GetValueOrDefault("artist") ?? "";
    public string UpcomingPath => Upcoming?.FilePath ?? "";
    public bool HasUpcoming => Upcoming is not null;
    public QueueViewModel(PlaybackSessionCoordinator session)
    {
        this.session = session; state = session.QueueState;
        session.QueueChanged += OnQueueChanged;
        Apply(state);
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
        foreach (var name in new[] { nameof(CountText), nameof(CanUndo), nameof(IsSequential), nameof(IsShuffle), nameof(IsRepeatOne),
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
        var album = BaseAlbum();
        var index = state.Items.ToList().FindIndex(x => x.ItemId == state.CurrentItemId);
        var source = ShowHistory ? state.History.Reverse().ToArray() : state.Items;
        Rows = source.Select((track, i) => new QueueRowViewModel(track, i, state.CurrentItemId, !ShowHistory && i < index, Artwork))
            .Where(row => ShowHistory || index < 0 || row.Index >= index || (!row.Track.QueuedNext && row.Track.Origin?.Id == album?.Id && album is not null))
            .Where(row => Search.Length == 0 || new[] { row.Title, row.Artist, row.Album, row.Source }.Any(value => value.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        foreach (var name in new[] { nameof(Heading), nameof(SectionTitle), nameof(ResultText), nameof(IsEmpty), nameof(EmptyTitle), nameof(EmptyHint), nameof(HasSelection) }) OnPropertyChanged(name);
    }
    partial void OnSearchChanged(string value) => RefreshRows();
    partial void OnShowHistoryChanged(bool value) { selection = []; RefreshRows(); }
    public void SetSelection(IEnumerable<QueueRowViewModel> rows)
    { selection = rows.Select(x => x.ItemId).Distinct().Take(1000).ToArray(); OnPropertyChanged(nameof(HasSelection)); }
    public IReadOnlyList<string> Selection => selection;
    [RelayCommand] private Task SequentialAsync() => RunAsync(() => session.SetQueueModeAsync(QueuePlaybackMode.Sequential, lifetime.Token));
    [RelayCommand] private Task ShuffleAsync() => RunAsync(() => session.SetQueueModeAsync(QueuePlaybackMode.Shuffle, lifetime.Token));
    [RelayCommand] private Task RepeatOneAsync() => RunAsync(() => session.SetQueueModeAsync(QueuePlaybackMode.RepeatOne, lifetime.Token));
    [RelayCommand] private Task RepeatAllAsync() => RunAsync(() => session.SetQueueModeAsync(QueuePlaybackMode.RepeatAll, lifetime.Token));
    [RelayCommand] private Task ClearAsync() => RunAsync(() => session.ClearQueueAsync(lifetime.Token), "已清空队列，当前歌曲继续播放。");
    [RelayCommand] private Task RemoveSelectedAsync() => RunAsync(() => session.RemoveQueueItemsAsync(selection, lifetime.Token), "已移除所选曲目。");
    [RelayCommand] private Task MoveNextAsync() => RunAsync(() => session.MoveQueueItemsNextAsync(selection, lifetime.Token), "已加入下一首播放。");
    [RelayCommand] private Task RemoveAfterPlayAsync() => RunAsync(() => session.SetRemoveAfterPlayAsync(selection, true, lifetime.Token), "所选曲目将在播放后移除。");
    [RelayCommand] private Task KeepAfterPlayAsync() => RunAsync(() => session.SetRemoveAfterPlayAsync(selection, false, lifetime.Token));
    [RelayCommand] private Task UndoAsync() => RunAsync(() => session.UndoQueueEditAsync(lifetime.Token), "已撤销队列编辑。");
    [RelayCommand] private Task PlayUpcomingAsync() => Upcoming is null ? Task.CompletedTask : RunAsync(() => session.SelectQueueItemAsync(Upcoming.ItemId, lifetime.Token));
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
        => RunAsync(() => session.EnqueueAsync(paths.Select(path => new QueueTrack(Guid.NewGuid().ToString("N"), Path.GetFullPath(path), Path.GetFullPath(path))), nextUp, lifetime.Token), "已添加到队列。");
    private async Task RunAsync(Func<Task> action, string success = "")
    {
        if (disposed || IsBusy) return;
        IsBusy = true; Message = "";
        try { await action(); if (!disposed) Message = success; }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) Message = $"操作未完成：{error.Message}"; }
        finally { if (!disposed) { IsBusy = false; OnPropertyChanged(nameof(CanUndo)); } }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel(); session.QueueChanged -= OnQueueChanged;
        pending = null; Rows = []; selection = [];
    }
}
