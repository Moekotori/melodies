using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Player;
using Echo.Playback;
using Avalonia.Threading;

namespace Echo.Desktop.Features.Albums;

public sealed partial class AlbumDetailViewModel(AlbumRow album, IAlbumLibrary library, PlayerViewModel player)
    : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _request;
    private bool _closed;
    private int _generation, _offset;
    private long? _revision;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanPlay))] private IReadOnlyList<AlbumTrackItem> tracks = [];
    [ObservableProperty] private string error = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanPlay))] private bool isBusy;
    [ObservableProperty] private bool favorite = album.Favorite;
    [ObservableProperty] private int rating = album.Rating;
    [ObservableProperty] private int activeTab;
    [ObservableProperty] private int sortIndex;
    [ObservableProperty] private bool descending;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanPlay))] private bool isLoading;
    public bool CanPlay => !IsBusy && !IsLoading && Tracks.Count > 0;
    private string? _playingId;
    public AlbumRow Album => album;
    public string Subtitle => $"{album.Artist} · {album.TrackCount:N0} 首 · {TimeSpan.FromSeconds(album.DurationSeconds).TotalMinutes:N0} 分钟" + (album.Year is { } year ? $" · {year}" : "");
    public string Meta => (album.Year is { } year ? $"{year}     " : "") + $"{album.TrackCount:N0} 首曲目";
    public string DurationLabel => $"{TimeSpan.FromSeconds(album.DurationSeconds).TotalMinutes:N0} 分钟";
    public string AddedDate => album.AddedUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(album.AddedUnix).ToLocalTime().ToString("yyyy.MM.dd") : "—";
    public string Quality => Tracks.FirstOrDefault()?.Signal ?? "";
    public string Summary => $"{album.TrackCount:N0} 首曲目     |     {DurationLabel}" + (Quality.Length > 0 ? "     |     " + Quality : "");
    public string Folder => Path.GetDirectoryName(album.SamplePath) ?? "";
    public string Heart => Favorite ? "♥" : "♡";
    public string HeartIcon => Favorite ? "heart-filled" : "heart";
    public string FavoriteAction => Favorite ? "取消收藏专辑" : "收藏专辑";
    public bool CanPersonalState => library is IAlbumPersonalState;
    public bool HasMultiplePages => album.TrackCount > 200;
    public bool IsTracks => ActiveTab == 0;
    public bool IsGallery => ActiveTab == 1;
    public bool IsSources => ActiveTab == 2;
    public bool IsVersions => ActiveTab == 3;
    public bool IsInformation => ActiveTab == 4;
    public event Action? PersonalStateChanged;
    partial void OnFavoriteChanged(bool value) { OnPropertyChanged(nameof(Heart)); OnPropertyChanged(nameof(HeartIcon)); OnPropertyChanged(nameof(FavoriteAction)); }
    partial void OnActiveTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsTracks)); OnPropertyChanged(nameof(IsGallery)); OnPropertyChanged(nameof(IsSources));
        OnPropertyChanged(nameof(IsVersions)); OnPropertyChanged(nameof(IsInformation));
    }
    public void ObservePlayback() { _playingId = player.Session.Snapshot.Current?.TrackId; player.Session.Changed += OnPlayback; }
    private void OnPlayback(PlaybackSnapshot snapshot)
    {
        var id = snapshot.Current?.TrackId;
        if (Interlocked.Exchange(ref _playingId, id) == id) return;
        Dispatcher.UIThread.Post(() => { if (!_lifetime.IsCancellationRequested) foreach (var track in Tracks.Where(track => track.IsPlaying || track.Row.Id == _playingId)) track.IsPlaying = track.Row.Id == _playingId; });
    }
    public string PageText => $"{(_offset / 200) + 1} / {Math.Max(1, (album.TrackCount + 199) / 200)}";
    public bool CanPrevious => _offset > 0 && !IsLoading;
    public bool CanNext => _offset + 200 < album.TrackCount && !IsLoading;
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext)); }
    partial void OnIsLoadingChanged(bool value) { OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext)); }
    partial void OnSortIndexChanged(int value) => _ = LoadAsync();
    partial void OnDescendingChanged(bool value) => _ = LoadAsync();

    public async Task LoadAsync(int offset = 0)
    {
        if (_closed) return;
        _request?.Cancel(); using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _request = request;
        var generation = ++_generation;
        IsLoading = true; Error = "";
        try
        {
            var page = await library.GetAlbumTracksAsync(new(album.Id, offset, Revision: _revision,
                Sort: (DetailTrackSort)Math.Clamp(SortIndex, 0, 3), Descending: Descending), request.Token);
            if (_lifetime.IsCancellationRequested || generation != _generation) return;
            _revision = page.Revision; _offset = offset;
            Tracks = page.Items.Select((row, index) => new AlbumTrackItem(row, offset + index + 1,
                library as IAlbumPersonalState, _lifetime.Token, message => Error = message) { IsPlaying = row.Id == _playingId }).ToArray();
            OnPropertyChanged(nameof(PageText));
            OnPropertyChanged(nameof(Quality)); OnPropertyChanged(nameof(Summary));
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (!_closed && generation == _generation) { _revision = null; Error = "曲库已变化，请刷新曲目。"; } }
        catch (Exception) { if (!_closed && generation == _generation) Error = "曲目读取失败，点击重试。"; }
        finally { if (generation == _generation) { IsLoading = false; _request = null; } }
    }
    [RelayCommand] private Task RetryAsync() { _revision = null; return LoadAsync(); }
    [RelayCommand] private Task PreviousAsync() => CanPrevious ? LoadAsync(_offset - 200) : Task.CompletedTask;
    [RelayCommand] private Task NextAsync() => CanNext ? LoadAsync(_offset + 200) : Task.CompletedTask;
    [RelayCommand] private Task PlayAsync() => PlayAlbumAsync(null);
    [RelayCommand] private Task ToggleFavoriteAsync() => SavePersonalAsync(!Favorite, Rating);
    public Task SetRatingAsync(int value) => SavePersonalAsync(Favorite, value == Rating ? 0 : value);
    private async Task SavePersonalAsync(bool favoriteValue, int ratingValue)
    {
        if (library is not IAlbumPersonalState state || IsBusy) return;
        IsBusy = true;
        try
        {
            await state.SetAlbumStateAsync(album.Id, favoriteValue, ratingValue, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            Favorite = favoriteValue; Rating = ratingValue; PersonalStateChanged?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_lifetime.IsCancellationRequested) Error = "收藏或评分保存失败，请重试。"; }
        finally { if (!_lifetime.IsCancellationRequested) IsBusy = false; }
    }
    [RelayCommand] private Task AddToQueueAsync() => PlayAlbumAsync(null, append: true);
    [RelayCommand] private Task PlayNextAsync() => PlayAlbumAsync(null, append: true, nextUp: true);
    public Task PlayTrackAsync(TrackRow track) => PlayAlbumAsync(track);
    public async Task AddTrackAsync(TrackRow track, bool nextUp)
    {
        if (IsBusy || _lifetime.IsCancellationRequested) return;
        IsBusy = true; Error = "";
        try { await player.Session.EnqueueAsync([ToQueue(track)], nextUp, _lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) Error = $"添加失败：{error.Message}"; }
        finally { if (!_lifetime.IsCancellationRequested) IsBusy = false; }
    }

    private async Task PlayAlbumAsync(TrackRow? selected, bool append = false, bool nextUp = false)
    {
        if (IsBusy || _lifetime.IsCancellationRequested) return;
        IsBusy = true; Error = "";
        try
        {
            if (album.TrackCount > 1000 && selected is null)
            { Error = "此专辑超过 1000 首，请选择单曲播放。"; return; }
            var queue = new List<QueueTrack>(Math.Min(1000, album.TrackCount));
            if (album.TrackCount > 1000) queue.Add(ToQueue(selected!));
            else
            {
                var sort = (DetailTrackSort)Math.Clamp(SortIndex, 0, 3); var reverse = Descending;
                long? revision = null;
                for (var offset = 0; offset < album.TrackCount; offset += 200)
                {
                    var page = await library.GetAlbumTracksAsync(new(album.Id, offset, Revision: revision,
                        Sort: sort, Descending: reverse), _lifetime.Token);
                    revision = page.Revision;
                    queue.AddRange(page.Items.Select(ToQueue));
                    if (page.Items.Count < 200) break;
                }
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            if (queue.Count > 0)
            {
                if (append) await player.Session.EnqueueAsync(queue, nextUp, _lifetime.Token);
                else await player.Session.SetQueueAsync(queue, selected is null ? queue[0].ItemId : queue.First(x => x.TrackId == selected.Id).ItemId, _lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { Error = "曲库已变化，请返回专辑墙刷新后播放。"; }
        catch (Exception) { if (!_lifetime.IsCancellationRequested) Error = "播放失败，请检查音源和输出设备后重试。"; }
        finally { if (!_lifetime.IsCancellationRequested) IsBusy = false; }
    }
    private QueueTrack ToQueue(TrackRow track) => new(Guid.NewGuid().ToString("N"), track.Id, track.Path,
        Source: Echo.Infrastructure.Library.TrackPlaybackSource.Audio(track), Metadata:
        new Dictionary<string, string?> { ["title"] = track.Title, ["artist"] = track.Artist, ["album"] = track.Album,
            ["duration"] = track.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), ["codec"] = track.Codec },
        Origin: new("album", album.Title, album.Id));
    public void Dispose() { if (_closed) return; _closed = true; player.Session.Changed -= OnPlayback; _request?.Cancel(); _lifetime.Cancel(); ++_generation; Tracks = []; PersonalStateChanged = null; }
}
