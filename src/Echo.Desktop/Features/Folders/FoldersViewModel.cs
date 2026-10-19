using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Shell;
using Echo.Playback;

namespace Echo.Desktop.Features.Folders;

public sealed partial class FoldersViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly IFolderLibrary _library;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _request;
    private int _generation;
    private long? _revision;
    private int _offset;
    private bool _closed;
    private string? _playingId;
    public FolderTreeState Tree { get; }
    public FolderWallState Wall { get; }
    public IReadOnlyList<FolderSortChoice> SortOptions { get; }
    public Echo.Desktop.Localization.LocalizationService Text => _shell.Text;
    [ObservableProperty] private FolderNode? selectedNode;
    [ObservableProperty] private FolderRow? currentFolder;
    [ObservableProperty] private IReadOnlyList<AlbumTrackItem> tracks = [];
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool recursive = true;
    [ObservableProperty] private bool losslessOnly;
    [ObservableProperty] private int sortIndex;
    [ObservableProperty] private bool descending;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isActing;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private long total;
    [ObservableProperty] private double duration;
    [ObservableProperty] private long sizeBytes;
    [ObservableProperty] private long losslessCount;
    public string Title => CurrentFolder?.Name ?? Text["folders.selectTitle"];
    public string PathLabel => CurrentFolder?.Path ?? Text["folders.selectHint"];
    public string Summary => Text.Format("folders.summary", Total, FormatDuration(Duration), FormatBytes(SizeBytes));
    public string HeaderSummary => CurrentFolder is { TrackCount: > 0 } folder
        ? Text.Format("folders.summary", folder.TrackCount, FormatDuration(folder.DurationSeconds), FormatBytes(folder.SizeBytes)) : Summary;
    public string Quality => Text.Format("folders.losslessCount", LosslessCount);
    public string PageText => Total == 0 ? Text["folders.zeroTracks"] : Text.Format("folders.page", _offset + 1, _offset + Tracks.Count, Total);
    public bool HasFolder => CurrentFolder is not null;
    public bool IsEmpty => !IsLoading && Tracks.Count == 0;
    public string EmptyTitle => HasFolder ? Text["folders.emptyTitle"] : Text["folders.startTitle"];
    public string EmptyHint => HasFolder ? Text["folders.emptyHint"] : Text["folders.selectHint"];
    public bool IsTreeEmpty => Tree.Nodes.Count == 0;
    public bool CanPrevious => !IsLoading && _offset > 0;
    public bool CanNext => !IsLoading && _offset + 200 < Total;
    public bool CanAct => !IsLoading && !IsActing && Total > 0 && _revision is not null;
    public IReadOnlyList<FolderRow> Breadcrumbs
    {
        get
        {
            if (CurrentFolder is null) return [];
            var crumbs = new List<FolderRow>();
            for (var node = SelectedNode; node is not null; node = node.Parent) crumbs.Add(node.Row);
            if (crumbs.Count == 0) crumbs.Add(CurrentFolder);
            crumbs.Reverse(); return crumbs;
        }
    }

    public FoldersViewModel(ShellViewModel shell)
    {
        _shell = shell; _library = shell.FolderLibrary!;
        SortOptions = new[] { "folders.sortPath", "folders.sortTitle", "folders.sortArtist", "folders.sortAlbum", "folders.sortDuration" }
            .Select(key => new FolderSortChoice(key, Text[key])).ToArray();
        Tree = new(_library, Text, message => Error = message);
        Wall = new(_library, Text, message => Error = message);
        Tree.Nodes.CollectionChanged += OnTreeChanged;
        Text.Changed += OnLanguageChanged;
        if (shell.Player is { } player)
        { _playingId = player.Session.Snapshot.Current?.TrackId; player.Session.Changed += OnPlayback; }
    }
    public async Task InitializeAsync()
    {
        await RestoreWorkbenchAsync();
        if (_closed) return;
        await Wall.LoadAsync(null);
    }
    partial void OnSelectedNodeChanged(FolderNode? value)
    {
        if (value is null) return;
        if (value.IsMore) { _ = Tree.ExpandAsync(value); return; }
        CurrentFolder = value.Row;
        _shell.LastFolderPath = value.Row.Path;
        ApplyFolderRule(value.Row); OnPropertyChanged(nameof(Breadcrumbs)); _ = LoadAsync();
    }
    partial void OnCurrentFolderChanged(FolderRow? value)
    { OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(PathLabel)); OnPropertyChanged(nameof(HeaderSummary)); OnPropertyChanged(nameof(HasFolder)); OnPropertyChanged(nameof(Breadcrumbs)); OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(EmptyHint)); }
    partial void OnSearchChanged(string value) => _ = LoadAsync(debounce: true);
    partial void OnRecursiveChanged(bool value) { _ = LoadAsync(); ScheduleRuleSave(); }
    partial void OnLosslessOnlyChanged(bool value) => _ = LoadAsync();
    partial void OnSortIndexChanged(int value) { _ = LoadAsync(); ScheduleRuleSave(); }
    partial void OnDescendingChanged(bool value) { _ = LoadAsync(); ScheduleRuleSave(); }
    partial void OnIsLoadingChanged(bool value) => NotifyPage();
    partial void OnIsActingChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    private void OnTreeChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) => OnPropertyChanged(nameof(IsTreeEmpty));
    private void OnLanguageChanged()
    {
        foreach (var option in SortOptions) option.Label = Text[option.Key];
        OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(PathLabel)); OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(EmptyHint)); NotifyPage();
    }
    private void NotifyPage()
    {
        foreach (var name in new[] { nameof(Summary), nameof(HeaderSummary), nameof(Quality), nameof(PageText), nameof(IsEmpty), nameof(CanPrevious), nameof(CanNext), nameof(CanAct) }) OnPropertyChanged(name);
    }
    private FolderTrackQuery Query(int offset = 0, long? revision = null) => new(CurrentFolder!.Path, offset,
        Search: Search, Recursive: Recursive, LosslessOnly: LosslessOnly, Sort: (FolderTrackSort)Math.Clamp(SortIndex, 0, 18),
        Descending: Descending, Revision: revision, Paths: CombinedFolders.Count > 0 ? CombinedFolders.Select(folder => folder.Path).ToArray() : null,
        HideDuplicates: HideDuplicates, Insight: ActiveInsight, RandomSeed: RandomSeed);

    public async Task LoadAsync(int offset = 0, bool debounce = false)
    {
        if (_closed || CurrentFolder is null) return;
        _request?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _request = request;
        var generation = ++_generation; var query = Query(offset, offset == 0 ? null : _revision);
        IsLoading = true; Error = ""; _revision = null; Tracks = []; NotifyPage();
        try
        {
            if (debounce) await Task.Delay(220, request.Token);
            var page = await _library.GetFolderTracksAsync(query, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (generation != _generation) return;
            _offset = offset; _revision = page.Revision; Total = page.Total; Duration = page.DurationSeconds;
            SizeBytes = page.SizeBytes; LosslessCount = page.LosslessCount;
            if (offset == 0 && query.Recursive && !query.LosslessOnly && string.IsNullOrWhiteSpace(query.Search) && CurrentFolder!.TrackCount == 0)
                CurrentFolder = CurrentFolder with { TrackCount = page.Total, DurationSeconds = page.DurationSeconds, SizeBytes = page.SizeBytes };
            Tracks = page.Items.Select((row, index) => new AlbumTrackItem(row, offset + index + 1,
                _library as IAlbumPersonalState, _lifetime.Token, message => Error = message) { IsPlaying = row.Id == _playingId }).ToArray();
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (generation == _generation) Error = Text["folders.changed"]; }
        catch (Exception) { if (!request.IsCancellationRequested) Error = Text["folders.readError"]; }
        finally { if (generation == _generation) { IsLoading = false; _request = null; NotifyPage(); } }
    }
    [RelayCommand] private Task PreviousAsync() => CanPrevious ? LoadAsync(_offset - 200) : Task.CompletedTask;
    [RelayCommand] private Task NextAsync() => CanNext ? LoadAsync(_offset + 200) : Task.CompletedTask;
    [RelayCommand] private Task RetryAsync() => RefreshAsync();
    [RelayCommand] private async Task RefreshAsync()
    { Error = ""; if (HasFolder) await LoadAsync(); else await Wall.RefreshAsync(); }
    [RelayCommand] private Task PlayAsync() => ActAsync();
    [RelayCommand] private Task ShuffleAsync() => ActAsync(shuffle: true);
    [RelayCommand] private Task AppendAsync() => ActAsync(append: true);
    [RelayCommand] private void ResetFilters() { Search = ""; LosslessOnly = false; Recursive = true; }
    public async Task OpenFolderAsync()
    {
        if (CurrentFolder is null || _shell.DetailResources is null) return;
        try { await _shell.DetailResources.OpenAsync(CurrentFolder.Path, _lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed) Error = Text["folders.openError"]; }
    }
    public void SelectBreadcrumb(FolderRow row)
    {
        for (var node = SelectedNode; node is not null; node = node.Parent)
            if (node.Row.Path == row.Path) { SelectedNode = node; return; }
        SelectedNode = new(row);
    }
    [RelayCommand] private void ShowRoots()
    {
        ++_generation; _request?.Cancel(); _request = null; SelectedNode = null; CurrentFolder = null;
        Tracks = []; Total = 0; _revision = null; IsLoading = false; Error = ""; _shell.LastFolderPath = null;
        _ = Wall.LoadAsync(null); NotifyPage();
    }
    [RelayCommand] private void BackFolder()
    { if (SelectedNode?.Parent is { } parent) SelectedNode = parent; else ShowRoots(); }
    private void OnPlayback(PlaybackSnapshot snapshot)
    {
        var id = snapshot.Current?.TrackId;
        if (Interlocked.Exchange(ref _playingId, id) == id) return;
        Dispatcher.UIThread.Post(() => { if (!_closed) foreach (var track in Tracks) track.IsPlaying = track.Row.Id == _playingId; });
    }
    public void Dispose()
    {
        if (_closed) return;
        ReleaseWorkbench();
        _closed = true; ++_generation; _request?.Cancel(); _lifetime.Cancel(); _lifetime.Dispose(); Wall.Dispose(); Tree.Dispose(); Tracks = [];
        Tree.Nodes.CollectionChanged -= OnTreeChanged; Text.Changed -= OnLanguageChanged;
        if (_shell.Player is { } player) player.Session.Changed -= OnPlayback;
    }
    private static string FormatDuration(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    private static string FormatBytes(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.##} GB" : $"{bytes / (double)(1L << 20):0.#} MB";
}
