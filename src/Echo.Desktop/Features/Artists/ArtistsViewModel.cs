using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.LibraryDetails;

namespace Echo.Desktop.Features.Artists;

public sealed partial class ArtistsViewModel : ObservableObject, IDisposable
{
    private readonly IArtistLibrary library;
    public ArtistsViewModel(IArtistLibrary library, LibraryViewPreferences? preference = null) { this.library = library;
        if (preference is not null) { sortIndex = Math.Clamp(preference.SortIndex,0,3); descending = preference.Descending; Filters.Restore(preference.Filter); }
        Filters.Changed += OnFiltersChanged; }
    public LibraryFilterViewModel Filters { get; } = new();
    public event Action? PreferenceChanged;
    public LibraryViewPreferences Preference => new("artists", SortIndex, Descending, Filter: Filters.Snapshot());
    private void OnFiltersChanged() { PreferenceChanged?.Invoke(); _ = LoadAsync(0, true); }
    private void OnOptions() { PreferenceChanged?.Invoke(); _ = LoadAsync(); }
    private CancellationTokenSource? _request;
    private bool _closed;
    private int _generation, _offset;
    private readonly Dictionary<int, ArtistPage> _pages = [];
    public long? Revision { get; private set; }
    public int ResidentArtists => _pages.Values.Sum(page => page.Items.Count);
    public event Action<bool>? Changed;
    [ObservableProperty] private bool showAlbumArtwork;
    [ObservableProperty] private IReadOnlyList<ArtistRow> items = [];
    [ObservableProperty] private string search = "";
    [ObservableProperty] private int sortIndex;
    [ObservableProperty] private int groupingIndex;
    [ObservableProperty] private bool descending;
    [ObservableProperty] private int total;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string error = "";
    public string CountText => IsBusy && Revision is null ? "正在读取艺术家…" : $"共 {Total:N0} 位艺术家";
    public string PageText => $"{_offset / 200 + 1} / {Math.Max(1, (Total + 199) / 200)}";
    public bool CanPrevious => !IsBusy && _offset > 0;
    public bool CanNext => !IsBusy && _offset + 200 < Total;
    public bool IsEmpty => !IsBusy && Items.Count == 0 && Error.Length == 0;
    partial void OnSearchChanged(string value) => _ = LoadAsync(0, true);
    partial void OnSortIndexChanged(int value) => OnOptions();
    partial void OnGroupingIndexChanged(int value) => _ = LoadAsync();
    partial void OnDescendingChanged(bool value) => OnOptions();
    partial void OnShowAlbumArtworkChanged(bool value) => Changed?.Invoke(false);
    partial void OnIsBusyChanged(bool value) => Notify();
    private void Notify()
    {
        OnPropertyChanged(nameof(CountText)); OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext)); OnPropertyChanged(nameof(IsEmpty));
    }
    [RelayCommand] public Task RefreshAsync() => LoadAsync();
    [RelayCommand] private Task PreviousAsync() => CanPrevious ? LoadAsync(_offset - 200) : Task.CompletedTask;
    [RelayCommand] private Task NextAsync() => CanNext ? LoadAsync(_offset + 200) : Task.CompletedTask;
    public async Task LoadAsync(int offset = 0, bool debounce = false)
    {
        if (_closed) return;
        _request?.Cancel();
        using var request = new CancellationTokenSource(); _request = request;
        var generation = ++_generation; IsBusy = true; Error = "";
        try
        {
            if (debounce) await Task.Delay(120, request.Token);
            var page = await library.GetArtistsAsync(new(offset, Search: Search, Sort: (ArtistSort)Math.Clamp(SortIndex, 0, 3),
                Descending: Descending, Grouping: (ArtistGrouping)Math.Clamp(GroupingIndex, 0, 1), Filter: Filters.Snapshot()), request.Token);
            if (_closed || generation != _generation) return;
            Items = page.Items; Total = page.Total; _offset = offset; Revision = page.Revision;
            _pages.Clear(); _pages[offset / 200] = page; Changed?.Invoke(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed && generation == _generation) Error = "艺术家读取失败，请刷新重试。"; }
        finally { if (generation == _generation) { _request = null; IsBusy = false; Notify(); } }
    }
    public ArtistRow? At(int index) => _pages.TryGetValue(index / 200, out var page) && index % 200 < page.Items.Count ? page.Items[index % 200] : null;
    public async Task LoadRangeAsync(int start, int count)
    {
        if (_closed || IsBusy || Revision is null || Total == 0 || count <= 0) return;
        var first = Math.Max(0, start / 200); var last = Math.Min((Total - 1) / 200, (start + count - 1) / 200);
        if (last < first || Enumerable.Range(first, last - first + 1).All(_pages.ContainsKey)) return;
        _request?.Cancel(); using var request = new CancellationTokenSource(); _request = request; var generation = ++_generation;
        try
        {
            for (var index = first; index <= last; index++)
            {
                if (_pages.ContainsKey(index)) continue;
                var page = await library.GetArtistsAsync(new(index * 200, Search: Search, Sort: (ArtistSort)Math.Clamp(SortIndex, 0, 3),
                    Descending: Descending, Grouping: (ArtistGrouping)Math.Clamp(GroupingIndex, 0, 1), Revision: Revision, Filter: Filters.Snapshot()), request.Token);
                if (_closed || generation != _generation) return;
                while (_pages.Count >= 3) _pages.Remove(_pages.Keys.MaxBy(key => Math.Abs(key - first)));
                _pages[index] = page; Items = page.Items;
            }
            Changed?.Invoke(false);
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (!_closed && generation == _generation) await LoadAsync(); }
        catch (Exception) { if (!_closed && generation == _generation) Error = "这一页读取失败，请刷新重试。"; }
        finally { if (ReferenceEquals(_request, request)) _request = null; }
    }
    public void Dispose() { PreferenceChanged = null; Filters.Changed -= OnFiltersChanged; _closed = true; ++_generation; _request?.Cancel(); _request = null; Items = []; _pages.Clear(); Changed = null; }
}
