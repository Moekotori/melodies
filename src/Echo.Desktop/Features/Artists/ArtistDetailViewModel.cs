using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Player;
using Echo.Playback;

namespace Echo.Desktop.Features.Artists;

public sealed partial class ArtistDetailViewModel : ObservableObject, IDisposable
{
    private readonly IArtistLibrary _library;
    private readonly IAlbumPersonalState? _personal;
    private readonly PlayerViewModel _player;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _request;
    private bool _closed;
    private long? _revision;
    private int _offset, _generation;
    private string? _playingId;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanPlay))] private IReadOnlyList<AlbumTrackItem> tracks = [];
    [ObservableProperty] private string error = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanPlay))] private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanPlay))] private bool isLoading;
    public bool CanPlay => !IsBusy && !IsLoading && Tracks.Count > 0;
    [ObservableProperty] private int activeTab;
    [ObservableProperty] private int sortIndex;
    [ObservableProperty] private bool descending;
    [ObservableProperty] private bool tracksExpanded = true;
    [ObservableProperty] private bool allTracks;
    public ArtistRow Artist { get; }
    public ArtistOverviewViewModel Overview { get; }
    private readonly Localization.LocalizationService? _text;
    public string Meta => _text?.Format("artist.content.meta", Artist.TrackCount, Artist.AlbumCount)
        ?? $"{Artist.TrackCount:N0} 首歌曲  ·  {Artist.AlbumCount:N0} 张专辑";
    public string DurationLabel => $"{TimeSpan.FromSeconds(Artist.DurationSeconds).TotalMinutes:N0} 分钟";
    public string Duration => DurationLabel;
    public bool IsOverview => ActiveTab == 0;
    public bool IsAlbums => ActiveTab == 1;
    public bool IsTracks => IsOverview && AllTracks;
    public bool ShowTracks => IsOverview && TracksExpanded;
    public bool CanExpandTracks => !AllTracks && Artist.TrackCount > 6;
    public bool HasTrackPages => AllTracks && HasMultiplePages;
    public IReadOnlyList<AlbumTrackItem> VisibleTracks => AllTracks ? Tracks : Tracks.Take(6).ToArray();
    public IReadOnlyList<ArtistHighlight> PreviewTracks => Tracks.Take(6).Select(track => new ArtistHighlight(track.Row, track.Row.Album, IsPlaying: track.IsPlaying)).ToArray();
    public double TrackListHeight => 448;
    public string TracksHeading => _text?.Format("artist.content.tracksHeading", Artist.Name) ?? $"{Artist.Name} 的歌曲";
    public string TracksCount => _text?.Format("artist.content.loadedTracks", VisibleTracks.Count, Artist.TrackCount) ?? $"{VisibleTracks.Count}/{Artist.TrackCount}";
    public string TracksToggle => _text?[TracksExpanded ? "artist.content.collapseTracks" : "artist.content.expandTracks"] ?? "歌曲";
    private void NotifyTrackContent()
    { foreach (var name in new[] { nameof(ShowTracks), nameof(VisibleTracks), nameof(IsTracks), nameof(TracksCount), nameof(TracksToggle), nameof(PreviewTracks), nameof(CanExpandTracks), nameof(HasTrackPages) }) OnPropertyChanged(name); }
    partial void OnTracksExpandedChanged(bool value) => NotifyTrackContent();
    partial void OnAllTracksChanged(bool value) => NotifyTrackContent();
    [RelayCommand] private void ToggleTracks() => TracksExpanded = !TracksExpanded;
    [RelayCommand] private void ExpandAll() { AllTracks = true; TracksExpanded = true; }
    public bool IsEmpty => !IsLoading && Error.Length == 0 && Tracks.Count == 0;
    partial void OnTracksChanging(IReadOnlyList<AlbumTrackItem> value)
    { foreach (var item in Tracks) item.PropertyChanged -= OnTrackState; }
    partial void OnTracksChanged(IReadOnlyList<AlbumTrackItem> value)
    {
        foreach (var item in value) item.PropertyChanged += OnTrackState;
        OnPropertyChanged(nameof(VisibleTracks)); OnPropertyChanged(nameof(TrackListHeight)); OnPropertyChanged(nameof(IsEmpty));
        NotifyTrackContent();
    }
    private void OnTrackState(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(AlbumTrackItem.Favorite) || sender is not AlbumTrackItem item) return;
        _revision = null;
        Overview.ApplyFavoriteChange(item.Favorite);
    }
    partial void OnActiveTabChanged(int value)
    {
        foreach (var name in new[] { nameof(IsOverview), nameof(IsAlbums), nameof(IsTracks), nameof(ShowTracks), nameof(VisibleTracks), nameof(TrackListHeight) }) OnPropertyChanged(name);
    }
    partial void OnSortIndexChanged(int value) => _ = LoadAsync();
    partial void OnDescendingChanged(bool value) => _ = LoadAsync();
    public string PageText => $"{_offset / 200 + 1} / {Math.Max(1, (Artist.TrackCount + 199) / 200)}";
    public bool CanPrevious => _offset > 0 && !IsLoading;
    public bool CanNext => _offset + 200 < Artist.TrackCount && !IsLoading;
    public bool HasMultiplePages => Artist.TrackCount > 200;
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext)); }
    partial void OnIsLoadingChanged(bool value) { OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext)); OnPropertyChanged(nameof(IsEmpty)); }
    public ArtistDetailViewModel(ArtistRow artist, IArtistLibrary library, IAlbumPersonalState? personal, PlayerViewModel player, Localization.LocalizationService? text = null)
    {
        Artist = artist; _library = library; _personal = personal; _player = player;
        _text = text; Overview = new(artist, library, text);
        if (_text is not null) _text.Changed += OnLanguage;
        _playingId = player.Session.Snapshot.Current?.TrackId;
        Overview.PlayingTrackId = _playingId;
        player.Session.Changed += OnPlayback;
    }
    private void OnLanguage() { OnPropertyChanged(nameof(Meta)); OnPropertyChanged(nameof(TracksHeading)); NotifyTrackContent(); }
    private void OnPlayback(PlaybackSnapshot snapshot)
    {
        if (Interlocked.Exchange(ref _playingId, snapshot.Current?.TrackId) == snapshot.Current?.TrackId) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed) return;
            foreach (var row in Tracks.Where(row => row.IsPlaying || row.Row.Id == _playingId)) row.IsPlaying = row.Row.Id == _playingId;
            Overview.PlayingTrackId = _playingId; OnPropertyChanged(nameof(PreviewTracks));
        });
    }
    public async Task LoadAsync(int offset = 0)
    {
        if (_closed) return;
        _request?.Cancel(); using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _request = request;
        var generation = ++_generation; IsLoading = true; Error = "";
        try
        {
            var page = await _library.GetArtistTracksAsync(new(Artist.Id, Artist.Grouping, offset, Revision: _revision,
                Sort: (DetailTrackSort)Math.Clamp(SortIndex, 0, 3), Descending: Descending), request.Token);
            if (_lifetime.IsCancellationRequested || generation != _generation) return;
            _revision = page.Revision; _offset = offset;
            Tracks = page.Items.Select((row, index) => new AlbumTrackItem(row, offset + index + 1, _personal,
                _lifetime.Token, value => Error = value) { IsPlaying = row.Id == _playingId }).ToArray();
            OnPropertyChanged(nameof(PageText));
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (!_closed && generation == _generation) { _revision = null; Error = "曲库已变化，请刷新曲目。"; } }
        catch (Exception) { if (!_closed && generation == _generation) Error = "曲目读取失败，请重试。"; }
        finally { if (generation == _generation) { IsLoading = false; _request = null; } }
    }
    [RelayCommand] private Task RetryAsync() { _revision = null; return LoadAsync(); }
    [RelayCommand] private Task PreviousAsync() => CanPrevious ? LoadAsync(_offset - 200) : Task.CompletedTask;
    [RelayCommand] private Task NextAsync() => CanNext ? LoadAsync(_offset + 200) : Task.CompletedTask;
    [RelayCommand] private Task PlayAsync() => PlayAsync(null, false, false);
    [RelayCommand] private Task ShuffleAsync() => PlayAsync(null, true, false);
    [RelayCommand] private Task AddToQueueAsync() => PlayAsync(null, false, true);
    public Task PlayTrackAsync(TrackRow track) => PlayAsync(track, false, false);
    public async Task AddTrackAsync(TrackRow track, bool next)
    {
        if (IsBusy) return;
        try { await _player.Session.EnqueueAsync([ToQueue(track)], next, _lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_lifetime.IsCancellationRequested) Error = "添加到队列失败，请重试。"; }
    }
    private async Task PlayAsync(TrackRow? selected, bool shuffle, bool append)
    {
        if (IsBusy || IsLoading || _lifetime.IsCancellationRequested) return;
        IsBusy = true; Error = ""; Status = "";
        try
        {
            var queue = new List<QueueTrack>(Math.Min(Artist.TrackCount, 1000));
            if (selected is not null) queue.Add(ToQueue(selected));
            else
            {
                var sort = (DetailTrackSort)Math.Clamp(SortIndex, 0, 3); var reverse = Descending;
                long? revision = null;
                for (var offset = 0; offset < Math.Min(1000, Artist.TrackCount); offset += 200)
                {
                    var page = await _library.GetArtistTracksAsync(new(Artist.Id, Artist.Grouping, offset, Revision: revision,
                        Sort: sort, Descending: reverse), _lifetime.Token);
                    revision = page.Revision; queue.AddRange(page.Items.Select(ToQueue));
                    if (page.Items.Count < 200) break;
                }
                if (Artist.TrackCount > 1000) Status = "本次队列包含前 1000 首；其他曲目可从列表选择播放。";
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            if (shuffle) Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(queue));
            if (queue.Count > 0)
            {
                if (append) await _player.Session.EnqueueAsync(queue, false, _lifetime.Token);
                else await _player.Session.SetQueueAsync(queue, queue[0].ItemId, _lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { Error = "曲库已变化，请刷新后再播放。"; }
        catch (Exception) { if (!_lifetime.IsCancellationRequested) Error = "播放失败，请检查音源和输出设备。"; }
        finally { if (!_lifetime.IsCancellationRequested) IsBusy = false; }
    }
    private QueueTrack ToQueue(TrackRow track) => new(Guid.NewGuid().ToString("N"), track.Id, track.Source?.Kind == "remote" ? track.Path : track.Source?.AudioPath ?? track.Path,
        Source: track.Source?.Kind == "remote" ? new AudioSource("remote", track.Path) : track.Source is { } source ? AudioSource.Local(source.AudioPath, source.StartSeconds, source.EndSeconds) : null,
        Metadata: new Dictionary<string, string?> { ["title"] = track.Title, ["artist"] = track.Artist, ["album"] = track.Album,
            ["duration"] = track.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), ["codec"] = track.Codec },
        Origin: new("artist", Artist.Name, Artist.Id), SampleRate: track.SampleRate > 0 ? track.SampleRate : null);
    public void Dispose() { if (_closed) return; _closed = true; if (_text is not null) _text.Changed -= OnLanguage; _player.Session.Changed -= OnPlayback; _request?.Cancel(); _lifetime.Cancel(); ++_generation; Tracks = []; Overview.Dispose(); }
}
