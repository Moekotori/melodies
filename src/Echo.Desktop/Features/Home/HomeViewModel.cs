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
    private readonly CancellationToken lifetimeToken;
    private CancellationTokenSource? request;
    private HomeDashboard? dashboard;
    private bool closed, refreshAgain, suspended, needsRefresh;
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
    public bool HasCollection => (dashboard?.Library.Tracks ?? 0) > 0;
    public bool ShowWelcome => !HasCollection && !HasFocus;
    public string WeekPlays => Number(dashboard?.Listening.WeekPlays ?? 0);
    public string WeekDuration => FormatDuration(dashboard?.Listening.WeekSeconds ?? 0);
    public string ActiveWeeks => Shell.Text.Format("home.activeWeeks", Days.Select(day => DateOnly.ParseExact(day.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
        .Select(date => date.AddDays(-(((int)date.DayOfWeek + 6) % 7))).Distinct().Count());
    public string ActiveDays => Shell.Text.Format("home.activeDays", dashboard?.Listening.ActiveDays ?? 0);
    public string AverageDuration => FormatDuration(dashboard?.Listening.AverageSessionSeconds ?? 0);
    public string CurrentStreak => Shell.Text.Format("home.activeDays", Streaks().Current);
    public string LongestStreak => Shell.Text.Format("home.activeDays", Streaks().Longest);
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
    public string HeroLabel => Shell.Text[Shell.Player is { HasTrack: true } ? "home.nowPlaying" : HasFocus ? "home.continueListening" : "home.startFromLibrary"];
    public string Specifications => Shell.Player is { HasTrack: true } player ? string.Join(" · ", new[] { player.Details.Format, player.Details.Specifications }.Where(s => s.Length > 0)) : dashboard?.LastTrack is { } track ? string.Join(" · ", new[] { track.Codec.ToUpperInvariant(), track.BitDepth > 0 ? $"{track.BitDepth} bit" : "", track.SampleRate > 0 ? $"{track.SampleRate / 1000d:0.##} kHz" : "" }.Where(value => value.Length > 0)) : "";
    public string SourceLabel => Shell.Player?.Session.Snapshot.Current?.Origin?.Label ?? "";
    public string RecentEmptyTitle => Shell.RecentEmptyTitle;
    public string RecentEmptyDescription => Shell.RecentEmptyDescription;

    public HomeViewModel(ShellViewModel shell)
    {
        lifetimeToken = lifetime.Token; Shell = shell; shell.PropertyChanged += OnShellChanged; shell.HomeDataChanged += OnDataChanged;
        shell.Text.Changed += Publish;
        if (shell.Player is { } player) { player.PropertyChanged += OnPlayerChanged; player.Details.PropertyChanged += OnPlayerChanged; }
    }
    public async Task RefreshAsync()
    {
        if (closed) return;
        if (suspended) { needsRefresh = true; return; }
        if (IsLoading) { refreshAgain = true; return; }
        if (Shell.HomeLibrary is not { } library) { Error = Shell.Text["home.noLibrary"]; return; }
        request?.Cancel(); using var current = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken); request = current;
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
            var album = await library.GetTrackAlbumAsync(FocusPath, lifetimeToken);
            if (!closed && album is not null) AlbumRequested?.Invoke(album);
            else if (!closed) Error = Shell.Text["home.albumMissing"];
        });
    }
    [RelayCommand] private Task ContinueAsync() => RunAsync(async () =>
    {
        if (Shell.Player is not { } player) return;
        if (player.HasTrack) { if (player.Session.Snapshot.Status != PlaybackStatus.Playing) await player.Session.ResumeAsync(lifetimeToken); }
        else if (dashboard?.LastTrack is { } track) await player.Session.PlayAsync(ToQueue(track), lifetimeToken);
    });
    [RelayCommand] private Task RandomAsync() => RunAsync(async () =>
    {
        if (Shell.HomeLibrary is not { } library || Shell.Player is not { } player) return;
        var rows = await library.GetRandomTracksAsync(lifetimeToken);
        if (rows.Count == 0) { Error = Shell.Text["home.emptyQueue"]; return; }
        await player.Session.SetQueueAsync(rows.Select(ToQueue), token: lifetimeToken);
    });
    [RelayCommand] private Task PlayTrackAsync(ListeningTrack? item) => item is null ? Task.CompletedTask : RunAsync(async () =>
    {
        if (Shell.Player is not { } player) return;
        var queued = player.Session.Queue.FirstOrDefault(track => track.TrackId == item.Track.Id);
        if (queued is not null) await player.Session.SelectQueueItemAsync(queued.ItemId, lifetimeToken);
        else await player.Session.PlayAsync(ToQueue(item.Track), lifetimeToken);
    });
    private async Task RunAsync(Func<Task> action)
    {
        if (closed || IsBusy) return;
        IsBusy = true; Error = "";
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) Error = Shell.Text["home.actionError"]; }
        finally { IsBusy = false; PublishFocus(); OnPropertyChanged(nameof(CanRandom)); }
    }
    private QueueTrack ToQueue(TrackRow track) => new(Guid.NewGuid().ToString("N"), track.Id, track.Path,
        Source: Echo.Infrastructure.Library.TrackPlaybackSource.Audio(track), Metadata:
        new Dictionary<string, string?> { ["title"] = track.Title, ["artist"] = track.Artist, ["album"] = track.Album, ["codec"] = track.Codec,
            ["duration"] = track.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) }, Origin: new("home", Shell.Text["home.librarySource"]));
    private string Number(long value) => value.ToString("N0", Shell.Text.Current.Culture);
    private (int Current, int Longest) Streaks()
    {
        var dates = Days.Select(day => DateOnly.ParseExact(day.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Order().ToArray();
        var longest = 0; var run = 0; DateOnly? last = null;
        foreach (var date in dates) { run = last?.AddDays(1) == date ? run + 1 : 1; longest = Math.Max(longest, run); last = date; }
        var active = dates.ToHashSet(); var cursor = DateOnly.FromDateTime(DateTime.Now); if (!active.Contains(cursor)) cursor = cursor.AddDays(-1);
        var current = 0; while (active.Contains(cursor)) { current++; cursor = cursor.AddDays(-1); }
        return (current, longest);
    }
    private string FormatDuration(double seconds) => seconds >= 3600 ? Shell.Text.Format("home.duration", (int)(seconds / 3600), (int)(seconds / 60) % 60)
        : Shell.Text.Format(seconds >= 60 ? "home.minutes" : "home.seconds", (int)(seconds >= 60 ? seconds / 60 : seconds));
    partial void OnRecentPageChanged(int value) { OnPropertyChanged(nameof(Recent)); OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext)); }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanContinue)); OnPropertyChanged(nameof(CanRandom)); }
    private void OnDataChanged() { if (suspended) needsRefresh = true; else _ = RefreshAsync(); }
    public void SetSuspended(bool value)
    {
        if (closed || suspended == value) return; suspended = value;
        if (value) { if (IsLoading) needsRefresh = true; request?.Cancel(); }
        else { PublishFocus(); if (needsRefresh) { needsRefresh = false; _ = RefreshAsync(); } }
    }
    private void OnShellChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(ShellViewModel.RecentTab)) { RecentPage = 0; Publish(); } }
    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    { if (!suspended && args.PropertyName is not ("Position" or "PositionText" or "Duration" or "DurationText" or "Volume" or "VolumeText")) PublishFocus(); }
    private void PublishFocus()
    { foreach (var name in new[] { nameof(FocusTitle), nameof(FocusArtist), nameof(FocusAlbum), nameof(FocusPath), nameof(HasFocus), nameof(ShowWelcome), nameof(HeroLabel), nameof(Specifications), nameof(SourceLabel), nameof(CanContinue) }) OnPropertyChanged(name); }
    private void Publish() { OnPropertyChanged(string.Empty); }
    public void Dispose()
    {
        if (closed) return; closed = true; lifetime.Cancel(); request?.Cancel(); lifetime.Dispose();
        Shell.PropertyChanged -= OnShellChanged; Shell.HomeDataChanged -= OnDataChanged; Shell.Text.Changed -= Publish;
        if (Shell.Player is { } player) { player.PropertyChanged -= OnPlayerChanged; player.Details.PropertyChanged -= OnPlayerChanged; }
        AlbumRequested = null; ArtistRequested = null; dashboard = null;
        // Active commands own linked cancellation until they finish; no new request is admitted.
    }
}
