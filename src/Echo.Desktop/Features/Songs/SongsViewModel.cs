using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Localization;
using Echo.Playback;

namespace Echo.Desktop.Features.Songs;

public sealed record TrackSortChoice(TrackSort? Field, string Label)
{ public override string ToString() => Label; }

public sealed partial class SongsViewModel : ObservableObject, IDisposable
{
    private readonly ITrackLibrary library;
    private readonly PlayerViewModel? player;
    private readonly LocalizationService text;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? request;
    private int generation, offset;
    private long? revision;
    private bool hasNext, closed;
    public LibraryFilterViewModel Filters { get; } = new();
    public IReadOnlyList<TrackSortChoice> SortChoices => Enum.GetValues<TrackSort>().Select(sortField => new TrackSortChoice(sortField, text["query.sort." + sortField])).ToArray();
    public IReadOnlyList<TrackSortChoice> AdditionalSortChoices => new[] { new TrackSortChoice(null, text["query.none"]) }.Concat(SortChoices).ToArray();
    [ObservableProperty] private IReadOnlyList<TrackRow> items = [];
    [ObservableProperty] private TrackRow? selected;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private int sortIndex;
    [ObservableProperty] private int secondaryIndex;
    [ObservableProperty] private int tertiaryIndex;
    [ObservableProperty] private bool descending;
    [ObservableProperty] private bool secondaryDescending;
    [ObservableProperty] private bool tertiaryDescending;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isActing;
    [ObservableProperty] private string error = "";
    public bool CanPrevious => !IsLoading && offset > 0;
    public bool CanNext => !IsLoading && hasNext;
    public bool IsEmpty => !IsLoading && Items.Count == 0 && Error.Length == 0;
    public bool HasQuery => Search.Length > 0 || Filters.HasActive;
    public string SortText => SortChoiceLabel(Math.Clamp(SortIndex,0,11), Descending)
        + (Order().Count > 1 ? " +" + (Order().Count - 1) : "");
    public string CountText => text.Format("query.visibleCount", Items.Count);
    public string PageText => text.Format("query.page", offset / 200 + 1, Items.Count);
    public SongsViewModel(ITrackLibrary library, PlayerViewModel? player, LocalizationService text, LibraryViewPreferences? preference = null)
    {
        this.library = library; this.player = player; this.text = text;
        Filters.AttachLibrary(library as IAlbumLibrary);
        if (preference is not null)
        {
            sortIndex = preference.SortIndex; descending = preference.Descending;
            secondaryIndex = preference.SecondaryIndex; secondaryDescending = preference.SecondaryDescending;
            tertiaryIndex = preference.TertiaryIndex; tertiaryDescending = preference.TertiaryDescending;
            Filters.Restore(preference.Filter);
        }
        isMultiSortEnabled = secondaryIndex > 0 || tertiaryIndex > 0;
        Filters.Changed += OnFilters; text.Changed += OnLanguage;
    }
    public event Action? PreferenceChanged;
    public LibraryViewPreferences Preference => new("songs", SortIndex, Descending, SecondaryIndex, SecondaryDescending, TertiaryIndex, TertiaryDescending, Filters.Snapshot());
    private void OnFilters() { OnPropertyChanged(nameof(HasQuery)); PreferenceChanged?.Invoke(); _ = LoadAsync(0, true); }
    private void OnOptions() { if (changingSort) return;
        if ((SecondaryIndex > 0 || TertiaryIndex > 0) && !IsMultiSortEnabled) { IsMultiSortEnabled = true; OnPropertyChanged(nameof(IsMultiSortEnabled)); }
        OnPropertyChanged(nameof(SortText)); OnPropertyChanged(nameof(SortOptions)); PreferenceChanged?.Invoke(); _ = LoadAsync(); }
    private void OnLanguage()
    { OnPropertyChanged(nameof(SortChoices)); OnPropertyChanged(nameof(AdditionalSortChoices)); OnPropertyChanged(nameof(SortOptions)); Notify(); }
    private IReadOnlyList<TrackOrder> Order()
    {
        var result = new List<TrackOrder> { new((TrackSort)Math.Clamp(SortIndex, 0, 11), Descending) };
        foreach (var (index, desc) in new[] { (SecondaryIndex, SecondaryDescending), (TertiaryIndex, TertiaryDescending) })
            if (index is >= 1 and <= 12 && result.All(order => order.Field != (TrackSort)(index - 1))) result.Add(new((TrackSort)(index - 1), desc));
        return result;
    }
    partial void OnSearchChanged(string value) { OnPropertyChanged(nameof(HasQuery)); _ = LoadAsync(0, true); }
    partial void OnSortIndexChanged(int value) => OnOptions();
    partial void OnSecondaryIndexChanged(int value) => OnOptions();
    partial void OnTertiaryIndexChanged(int value) => OnOptions();
    partial void OnDescendingChanged(bool value) => OnOptions();
    partial void OnSecondaryDescendingChanged(bool value) => OnOptions();
    partial void OnTertiaryDescendingChanged(bool value) => OnOptions();
    partial void OnIsLoadingChanged(bool value) => Notify();
    private void Notify()
    {
        OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(CountText)); OnPropertyChanged(nameof(PageText)); OnPropertyChanged(nameof(SortText));
    }
    [RelayCommand] private Task RefreshAsync() => LoadAsync();
    [RelayCommand] private Task PreviousAsync() => CanPrevious ? LoadAsync(offset - 200, expected: revision) : Task.CompletedTask;
    [RelayCommand] private Task NextAsync() => CanNext ? LoadAsync(offset + 200, expected: revision) : Task.CompletedTask;
    [RelayCommand] private void ClearSearch() => Search = "";
    [RelayCommand] private void ResetQuery() { Search = ""; Filters.Reset(); }
    public async Task LoadAsync(int target = 0, bool debounce = false, long? expected = null)
    {
        if (closed) return;
        request?.Cancel(); using var current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        request = current; var version = ++generation; IsLoading = true; Error = "";
        // Disable actions on results belonging to a previous query.
        Selected = null; Items = [];
        try
        {
            if (debounce) await Task.Delay(120, current.Token);
            var page = await library.GetTracksAsync(new(200, Search, Order: Order(), Filter: Filters.Snapshot(), Offset: target, Revision: expected), current.Token);
            if (closed || version != generation) return;
            Items = page.Items; offset = target; revision = page.Revision; hasNext = page.Next is not null;
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (version == generation && !closed) await LoadAsync(); }
        catch (ArgumentException) { if (version == generation && !closed) Error = text["query.invalid"]; }
        catch (Exception) { if (version == generation && !closed) Error = text["query.failed"]; }
        finally { if (version == generation) { request = null; IsLoading = false; Notify(); } }
    }
    [RelayCommand] private Task PlayAsync() => ActAsync(false, false);
    [RelayCommand] private Task AddAsync() => ActAsync(true, false);
    [RelayCommand] private Task PlayNextAsync() => ActAsync(true, true);
    private async Task ActAsync(bool append, bool next)
    {
        if (Selected is not { } selected || player is null || IsLoading || IsActing || closed) return;
        IsActing = true; Error = "";
        try
        {
            // Only the visible page is enqueued; the library never resides in the UI.
            var rows = append ? new[] { selected } : Items;
            var queue = rows.Select(row => new QueueTrack(Guid.NewGuid().ToString("N"), row.Id, row.Path,
                Metadata: new Dictionary<string, string?> { ["title"] = row.Title, ["artist"] = row.Artist, ["album"] = row.Album,
                    ["duration"] = row.DurationSeconds.ToString(CultureInfo.InvariantCulture), ["codec"] = row.Codec },
                Origin: new("songs", text["navigation.songs"]), SampleRate: row.SampleRate > 0 ? row.SampleRate : null)).ToArray();
            if (append) await player.Session.EnqueueAsync(queue, next, lifetime.Token);
            else await player.Session.SetQueueAsync(queue, queue.First(track => track.TrackId == selected.Id).ItemId, lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) Error = text["query.playFailed"]; }
        finally { if (!closed) IsActing = false; }
    }
    [RelayCommand] private async Task FavoriteAsync()
    {
        if (Selected is not { } row || library is not IAlbumPersonalState state || IsActing || closed) return;
        IsActing = true;
        try { await state.SetTrackFavoriteAsync(row.Id, !row.Favorite, lifetime.Token); await LoadAsync(offset); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) Error = text["query.failed"]; }
        finally { if (!closed) IsActing = false; }
    }
    public void Dispose()
    {
        if (closed) return; closed = true; ++generation; request?.Cancel(); lifetime.Cancel(); lifetime.Dispose();
        PreferenceChanged = null; Filters.Changed -= OnFilters; text.Changed -= OnLanguage; Items = []; Selected = null;
    }
}
