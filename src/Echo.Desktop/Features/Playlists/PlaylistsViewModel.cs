using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Playlists;

// Page owns two cancellable 200-row windows, never the whole library or its connections.
public sealed partial class PlaylistsViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel shell;
    private readonly IPlaylistLibrary library;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? browseRequest, trackRequest;
    private int browseGeneration, trackGeneration, browseOffset, trackOffset;
    private long browseRevision, trackRevision;
    private bool closed;
    public LocalizationService Text => shell.Text;
    [ObservableProperty] private IReadOnlyList<PlaylistRow> playlists = [];
    [ObservableProperty] private IReadOnlyList<PlaylistTrackItem> tracks = [];
    [ObservableProperty] private PlaylistRow? selectedPlaylist;
    [ObservableProperty] private string selection = "home";
    [ObservableProperty] private string playlistSearch = "";
    [ObservableProperty] private string search = "";
    [ObservableProperty] private int sortIndex;
    [ObservableProperty] private bool descending;
    [ObservableProperty] private bool cards;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isBrowsing;
    [ObservableProperty] private bool isActing;
    [ObservableProperty] private long totalPlaylists;
    [ObservableProperty] private long likedTracks;
    [ObservableProperty] private long likedAlbums;
    [ObservableProperty] private long totalTracks;
    [ObservableProperty] private double durationSeconds;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private string status = "";
    public bool IsHome => Selection == "home";
    public bool IsLiked => Selection == "liked";
    public bool IsAlbums => Selection == "albums";
    public bool IsManual => SelectedPlaylist is not null && !IsHome && !IsLiked && !IsAlbums;
    public bool CanAct => !IsLoading && !IsActing && !closed;
    public bool CanPlay => CanAct && Tracks.Count > 0 && shell.Player is not null;
    public bool CanMove => CanAct && IsManual && SortIndex == 0 && !Descending && Search.Length == 0;
    public bool CanUndo => shell.LastArchivedPlaylist is not null && !IsActing;
    public bool CanBrowsePrevious => !IsBrowsing && browseOffset > 0;
    public bool CanBrowseNext => !IsBrowsing && browseOffset + Playlists.Count < TotalPlaylists;
    public bool CanPrevious => !IsLoading && trackOffset > 0;
    public bool CanNext => !IsLoading && trackOffset + Tracks.Count < TotalTracks;
    public bool IsEmpty => !IsLoading && Tracks.Count == 0;
    public bool IsBrowseEmpty => !IsBrowsing && Playlists.Count == 0 && !string.IsNullOrWhiteSpace(PlaylistSearch);
    public bool IsFirstBrowsePage => browseOffset == 0;
    public bool HasBrowsePages => CanBrowsePrevious || CanBrowseNext;
    public string Title => IsHome ? Text["playlists.mine"] : IsLiked ? Text["playlists.liked"] : IsAlbums ? Text["playlists.albums"] : SelectedPlaylist?.Name ?? "";
    public string Description => IsLiked ? Text["playlists.likedHint"] : SelectedPlaylist?.Description ?? "";
    public string SourceLabel => Text[IsManual ? "playlists.local" : "playlists.favorites"];
    public string Summary => Text.Format("playlists.summary", TotalTracks, TimeSpan.FromSeconds(Math.Max(0, DurationSeconds)).TotalMinutes.ToString("N0"));
    public string BrowseSummary => Text.Format("playlists.count", TotalPlaylists);
    public string BrowsePage => Text.Format("playlists.page", browseOffset / 200 + 1);
    public string TrackPage => Text.Format("playlists.page", trackOffset / 200 + 1);
    private bool PlaysCurrentPage => TotalTracks > 1000 || library is not IPlaylistPlayback && TotalTracks > Tracks.Count;
    public string PlayLabel => Text[PlaysCurrentPage ? "playlists.playPage" : "playlists.play"];
    public string ShuffleLabel => Text[PlaysCurrentPage ? "playlists.shufflePage" : "playlists.shuffle"];
    public string AppendLabel => Text[PlaysCurrentPage ? "playlists.appendPage" : "playlists.append"];
    public string? CoverPath => IsManual ? SelectedPlaylist?.SamplePath : Tracks.FirstOrDefault()?.Row.Path;
    public event Action? SelectionChanged;
    public PlaylistsViewModel(ShellViewModel shell)
    {
        this.shell = shell; library = shell.PlaylistLibrary!; cards = shell.PlaylistCards;
        Text.Changed += OnLanguage;
    }
    public async Task InitializeAsync()
    {
        await LoadBrowserAsync(); if (closed) return;
        var remembered = shell.PlaylistSelection;
        if (remembered is "liked" or "albums") Open(remembered);
        else if (Playlists.FirstOrDefault(row => row.Id == remembered) is { } row) OpenPlaylist(row);
    }
    public void OpenPlaylist(PlaylistRow row) { SelectedPlaylist = row; Open(row.Id); }
    [RelayCommand] public void Open(string target)
    {
        if (closed) return;
        trackRequest?.Cancel(); ++trackGeneration; Tracks = []; resettingSelection = true;
        Search = ""; SortIndex = 0; Descending = false; resettingSelection = false;
        Selection = target; shell.PlaylistSelection = target; trackOffset = 0; TotalTracks = 0; DurationSeconds = 0; Error = ""; IsLoading = false;
        Notify(); SelectionChanged?.Invoke();
        if (!IsHome && !IsAlbums) _ = LoadTracksAsync();
    }
    partial void OnSelectedPlaylistChanged(PlaylistRow? value) => Notify();
    partial void OnCardsChanged(bool value) { shell.PlaylistCards = value; }
    partial void OnPlaylistSearchChanged(string value) => _ = LoadBrowserAsync(debounce: true);
    partial void OnSearchChanged(string value) { if (!resettingSelection && !IsHome && !IsAlbums) _ = LoadTracksAsync(debounce: true); }
    private bool resettingSelection;
    partial void OnSortIndexChanged(int value) { if (!resettingSelection && !IsHome && !IsAlbums) _ = LoadTracksAsync(); }
    partial void OnDescendingChanged(bool value) { if (!resettingSelection && !IsHome && !IsAlbums) _ = LoadTracksAsync(); }
    partial void OnIsLoadingChanged(bool value) => Notify();
    partial void OnIsBrowsingChanged(bool value) => Notify();
    partial void OnIsActingChanged(bool value) => Notify();
    private void OnLanguage() => Notify();
    private void Notify()
    {
        foreach (var name in new[] { nameof(IsHome), nameof(IsLiked), nameof(IsAlbums), nameof(IsManual), nameof(Title), nameof(Description),
            nameof(Summary), nameof(SourceLabel), nameof(BrowseSummary), nameof(CanAct), nameof(CanPlay), nameof(CanMove), nameof(CanUndo), nameof(CanPrevious), nameof(CanTransfer), nameof(CanExport),
            nameof(CanNext), nameof(HasBrowsePages), nameof(IsFirstBrowsePage), nameof(CanBrowsePrevious), nameof(CanBrowseNext), nameof(IsEmpty), nameof(IsBrowseEmpty), nameof(BrowsePage), nameof(TrackPage), nameof(PlayLabel), nameof(ShuffleLabel), nameof(AppendLabel), nameof(CoverPath) }) OnPropertyChanged(name);
    }
    public async Task LoadBrowserAsync(int offset = 0, bool debounce = false, long? expected = null)
    {
        if (closed) return; browseRequest?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); browseRequest = request;
        var generation = ++browseGeneration; IsBrowsing = true;
        try
        {
            if (debounce) await Task.Delay(120, request.Token);
            var page = await library.GetPlaylistsAsync(new(offset, Search: PlaylistSearch, Revision: expected), request.Token);
            if (closed || generation != browseGeneration) return;
            Playlists = page.Items; TotalPlaylists = page.Total; LikedTracks = page.LikedTracks; LikedAlbums = page.LikedAlbums;
            browseOffset = offset; browseRevision = page.Revision;
            if (SelectedPlaylist is { } old && page.Items.FirstOrDefault(row => row.Id == old.Id) is { } updated) SelectedPlaylist = updated;
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (!closed && generation == browseGeneration) await LoadBrowserAsync(); }
        catch (Exception) { if (!closed && generation == browseGeneration) Error = Text["playlists.loadError"]; }
        finally { if (generation == browseGeneration) { browseRequest = null; IsBrowsing = false; Notify(); } }
    }
    public async Task LoadTracksAsync(int offset = 0, bool debounce = false, long? expected = null)
    {
        if (closed || IsHome || IsAlbums) return; trackRequest?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); trackRequest = request;
        var generation = ++trackGeneration; IsLoading = true; Error = "";
        try
        {
            if (debounce) await Task.Delay(120, request.Token);
            var page = await library.GetPlaylistTracksAsync(new(IsLiked ? null : Selection, offset, Search: Search,
                Sort: (PlaylistTrackSort)SortIndex, Descending: Descending, Revision: expected), request.Token);
            if (closed || generation != trackGeneration) return;
            Tracks = page.Items.Select((row, index) => new PlaylistTrackItem(row, offset + index + 1, ToggleFavoriteAsync)).ToArray();
            TotalTracks = page.Total; DurationSeconds = page.DurationSeconds; trackOffset = offset; trackRevision = page.Revision;
            UpdatePlaying();
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (!closed && generation == trackGeneration) await LoadTracksAsync(); }
        catch (Exception) { if (!closed && generation == trackGeneration) Error = Text["playlists.loadError"]; }
        finally { if (generation == trackGeneration) { trackRequest = null; IsLoading = false; Notify(); } }
    }
    public void UpdatePlaying()
    {
        var path = shell.Player?.CurrentFilePath;
        foreach (var item in Tracks) item.IsPlaying = string.Equals(item.Row.Path, path, StringComparison.Ordinal);
    }
    [RelayCommand] private async Task RefreshAsync() { Error = ""; await LoadBrowserAsync(browseOffset); await LoadTracksAsync(); }
    [RelayCommand] private Task BrowsePreviousAsync() => CanBrowsePrevious ? LoadBrowserAsync(browseOffset - 200, expected: browseRevision) : Task.CompletedTask;
    [RelayCommand] private Task BrowseNextAsync() => CanBrowseNext ? LoadBrowserAsync(browseOffset + 200, expected: browseRevision) : Task.CompletedTask;
    [RelayCommand] private Task PreviousAsync() => CanPrevious ? LoadTracksAsync(trackOffset - 200, expected: trackRevision) : Task.CompletedTask;
    [RelayCommand] private Task NextAsync() => CanNext ? LoadTracksAsync(trackOffset + 200, expected: trackRevision) : Task.CompletedTask;
    public void Dispose()
    {
        if (closed) return; closed = true; ++browseGeneration; ++trackGeneration;
        browseRequest?.Cancel(); trackRequest?.Cancel(); lifetime.Cancel(); lifetime.Dispose();
        Text.Changed -= OnLanguage; Tracks = []; Playlists = []; SelectionChanged = null;
    }
}
