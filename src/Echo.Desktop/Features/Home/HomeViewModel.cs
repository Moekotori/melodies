using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;
using Echo.Playback;

namespace Echo.Desktop.Features.Home;

// Only one bounded dashboard snapshot and the four visible recent albums are resident.
public sealed partial class HomeViewModel : ObservableObject, IDisposable
{
    public ShellViewModel Shell { get; }
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? request;
    private HomeDashboard? dashboard;
    private bool closed, refreshAgain;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private int recentPage;
    public event Action<AlbumRow>? AlbumRequested;
    public event Action<string>? ArtistRequested;
    public IReadOnlyList<AlbumRow> Recent => (Shell.RecentTab == "played" ? dashboard?.RecentPlayed : dashboard?.RecentAdded)?.Skip(RecentPage * 4).Take(4).ToArray() ?? [];
    public IReadOnlyList<AlbumRow> Recommended => dashboard?.Recommended ?? [];
    public IReadOnlyList<AlbumRow> Rediscovery => dashboard?.Listening.Albums.Where(a => a.LastPlayedUnix < DateTimeOffset.Now.AddDays(-7).ToUnixTimeSeconds()).Select(a => a.Album).Take(4).ToArray() ?? [];
    public IReadOnlyList<ListeningArtist> TopArtists => dashboard?.Listening.Artists ?? [];
    public IReadOnlyList<ListeningTrack> TopTracks => dashboard?.Listening.Tracks ?? [];
    public IReadOnlyList<ListeningAlbum> TopAlbums => dashboard?.Listening.Albums ?? [];
    public IReadOnlyList<ListeningQuality> Quality => dashboard?.Listening.Quality ?? [];
    public IReadOnlyList<ListeningDay> Days => dashboard?.Listening.Days ?? [];
    public string ArtistCount => Number(dashboard?.Library.Artists ?? 0);
    public string AlbumCount => Number(dashboard?.Library.Albums ?? 0);
    public string TrackCount => Number(dashboard?.Library.Tracks ?? 0);
    public string FolderCount => Number(dashboard?.Library.Folders ?? 0);
    public string WeekPlays => Number(dashboard?.Listening.WeekPlays ?? 0);
    public string WeekDuration => FormatDuration(dashboard?.Listening.WeekSeconds ?? 0);
    public string ActiveDays => Shell.Text.Format("home.activeDays", dashboard?.Listening.ActiveDays ?? 0);
    public string AverageDuration => FormatDuration(dashboard?.Listening.AverageSeconds ?? 0);
    public string RepeatRate => (dashboard?.Listening.RepeatRate ?? 0).ToString("P0", Shell.Text.Current.Culture);
    public bool HasListening => Days.Count > 0;
    public bool HasRecent => Recent.Count > 0;
    public bool HasRecommendations => Recommended.Count > 0;
    public bool HasRediscovery => Rediscovery.Count > 0;
    public bool CanPrevious => RecentPage > 0;
    public bool CanNext => (RecentPage + 1) * 4 < (Shell.RecentTab == "played" ? dashboard?.RecentPlayed.Count : dashboard?.RecentAdded.Count);
    public bool CanRandom => (dashboard?.Library.Tracks ?? 0) > 0 && !IsBusy;
    public bool CanContinue => Shell.Player is { IsConnected: true } player && (player.HasTrack || dashboard?.LastTrack is not null) && !IsBusy;
    public string FocusTitle => Shell.Player is { HasTrack: true } player ? player.Title : dashboard?.LastTrack?.Title ?? Shell.Text["home.emptyTitle"];
    public string FocusArtist => Shell.Player is { HasTrack: true } player ? player.Details.Artist : dashboard?.LastTrack?.Artist ?? "";
    public string FocusAlbum => Shell.Player is { HasTrack: true } player ? player.Details.Album : dashboard?.LastTrack?.Album ?? "";
    public string FocusPath => Shell.Player is { HasTrack: true } player ? player.CurrentFilePath : dashboard?.LastTrack?.Path ?? "";
    public bool HasFocus => FocusPath.Length > 0;
    public string HeroLabel => Shell.Text[Shell.Player is { HasTrack: true } ? "home.nowPlaying" : "home.continueListening"];
    public string Specifications => Shell.Player is { HasTrack: true } player ? string.Join(" · ", new[] { player.Details.Format, player.Details.Specifications }.Where(s => s.Length > 0)) : dashboard?.LastTrack is { } track ? $"{track.Codec.ToUpperInvariant()} · {track.BitDepth} bit · {track.SampleRate / 1000d:0.##} kHz" : "";
    public string SourceLabel => Shell.Player?.Session.Snapshot.Current?.Origin?.Label ?? "";
    public string RecentEmptyTitle => Shell.RecentEmptyTitle;
    public string RecentEmptyDescription => Shell.RecentEmptyDescription;

    public HomeViewModel(ShellViewModel shell)
    {
        Shell = shell; shell.PropertyChanged += OnShellChanged; shell.HomeDataChanged += OnDataChanged;
        shell.Text.Changed += Publish;
        if (shell.Player is { } player) { player.PropertyChanged += OnPlayerChanged; player.Details.PropertyChanged += OnPlayerChanged; }
    }
    public async Task RefreshAsync()
    {
        if (closed) return;
        if (IsLoading) { refreshAgain = true; return; }
        if (Shell.HomeLibrary is not { } library) { Error = Shell.Text["home.noLibrary"]; return; }
        request?.Cancel(); using var current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); request = current;
        IsLoading = true; Error = "";
        try
        {
            var loaded = await library.GetHomeAsync(DateOnly.FromDateTime(DateTime.Now), current.Token);
            if (closed || current.IsCancellationRequested) return;
            dashboard = loaded; RecentPage = Math.Min(RecentPage, Math.Max(0, ((Shell.RecentTab == "played" ? loaded.RecentPlayed.Count : loaded.RecentAdded.Count) - 1) / 4)); Publish();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) Error = Shell.Text["home.loadError"]; }
        finally
        {
            if (ReferenceEquals(request, current)) request = null;
            IsLoading = false;
            if (refreshAgain && !closed) { refreshAgain = false; _ = RefreshAsync(); }
        }
    }
    [RelayCommand] private Task Refresh() => RefreshAsync();
    [RelayCommand] private void PreviousRecent() { if (CanPrevious) RecentPage--; }
    [RelayCommand] private void NextRecent() { if (CanNext) RecentPage++; }
    [RelayCommand] private void OpenAlbum(AlbumRow? album) { if (album is not null) AlbumRequested?.Invoke(album); }
    [RelayCommand] private void OpenArtist(string? name) { if (!string.IsNullOrWhiteSpace(name)) ArtistRequested?.Invoke(name); }
    [RelayCommand] private async Task OpenFocusAlbumAsync()
    {
        if (FocusPath.Length == 0 || Shell.HomeLibrary is not { } library) return;
        await RunAsync(async () =>
        {
            var album = await library.GetTrackAlbumAsync(FocusPath, lifetime.Token);
            if (!closed && album is not null) AlbumRequested?.Invoke(album);
            else if (!closed) Error = Shell.Text["home.albumMissing"];
        });
    }
    [RelayCommand] private Task ContinueAsync() => RunAsync(async () =>
    {
        if (Shell.Player is not { } player) return;
        if (player.HasTrack) { if (player.Session.Snapshot.Status != PlaybackStatus.Playing) await player.Session.ResumeAsync(lifetime.Token); }
        else if (dashboard?.LastTrack is { } track) await player.Session.PlayAsync(ToQueue(track), lifetime.Token);
    });
    [RelayCommand] private Task RandomAsync() => RunAsync(async () =>
    {
        if (Shell.HomeLibrary is not { } library || Shell.Player is not { } player) return;
        var rows = await library.GetRandomTracksAsync(lifetime.Token);
        if (rows.Count == 0) { Error = Shell.Text["home.emptyQueue"]; return; }
        await player.Session.SetQueueAsync(rows.Select(ToQueue), token: lifetime.Token);
    });
    [RelayCommand] private Task PlayTrackAsync(ListeningTrack? item) => item is null ? Task.CompletedTask : RunAsync(async () =>
    {
        if (Shell.Player is not { } player) return;
        var queued = player.Session.Queue.FirstOrDefault(track => track.TrackId == item.Track.Id);
        if (queued is not null) await player.Session.SelectQueueItemAsync(queued.ItemId, lifetime.Token);
        else await player.Session.PlayAsync(ToQueue(item.Track), lifetime.Token);
    });
    private async Task RunAsync(Func<Task> action)
    {
        if (closed || IsBusy) return;
        IsBusy = true; Error = "";
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) Error = Shell.Text["home.actionError"]; }
        finally { IsBusy = false; Publish(); }
    }
    private QueueTrack ToQueue(TrackRow track) => new(Guid.NewGuid().ToString("N"), track.Id, track.Path,
        Source: Echo.Infrastructure.Library.TrackPlaybackSource.Audio(track), Metadata:
        new Dictionary<string, string?> { ["title"] = track.Title, ["artist"] = track.Artist, ["album"] = track.Album, ["codec"] = track.Codec,
            ["duration"] = track.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) }, Origin: new("home", Shell.Text["home.librarySource"]));
    private string Number(long value) => value.ToString("N0", Shell.Text.Current.Culture);
    private string FormatDuration(double seconds) => Shell.Text.Format("home.duration", (int)(seconds / 3600), (int)(seconds / 60) % 60);
    partial void OnRecentPageChanged(int value) { OnPropertyChanged(nameof(Recent)); OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext)); }
    private void OnDataChanged() => _ = RefreshAsync();
    private void OnShellChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(ShellViewModel.RecentTab)) { RecentPage = 0; Publish(); } }
    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is not ("Position" or "PositionText" or "Duration" or "DurationText" or "Volume" or "VolumeText")) PublishFocus(); }
    private void PublishFocus()
    { foreach (var name in new[] { nameof(FocusTitle), nameof(FocusArtist), nameof(FocusAlbum), nameof(FocusPath), nameof(HasFocus), nameof(HeroLabel), nameof(Specifications), nameof(SourceLabel), nameof(CanContinue) }) OnPropertyChanged(name); }
    private void Publish() { OnPropertyChanged(string.Empty); }
    public void Dispose()
    {
        if (closed) return; closed = true; lifetime.Cancel(); request?.Cancel();
        Shell.PropertyChanged -= OnShellChanged; Shell.HomeDataChanged -= OnDataChanged; Shell.Text.Changed -= Publish;
        if (Shell.Player is { } player) { player.PropertyChanged -= OnPlayerChanged; player.Details.PropertyChanged -= OnPlayerChanged; }
        AlbumRequested = null; ArtistRequested = null; dashboard = null;
        // Active commands own linked cancellation until they finish; no new request is admitted.
    }
}
