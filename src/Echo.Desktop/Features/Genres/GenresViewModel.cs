using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Contracts.Settings;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Genres;

public sealed partial class GenresViewModel : ObservableObject, IDisposable
{
    private readonly IGenreLibrary library;
    public GenresViewModel(IGenreLibrary library, LibraryViewPreferences? preference = null, LocalizationService? text = null) { this.library = library;
        if (preference is not null) { sortIndex = Math.Clamp(preference.SortIndex,0,2); descending = preference.Descending; }
        Text = text ?? new(); Text.Changed += Notify; }
    public LocalizationService Text { get; }
    public event Action? PreferenceChanged;
    public LibraryViewPreferences Preference => new("genres", SortIndex, Descending);
    private void OnOptions() { PreferenceChanged?.Invoke(); _ = LoadAsync(); }
    private CancellationTokenSource? _request;
    private bool _closed;
    private int _generation, _offset;
    private readonly Dictionary<int, GenrePage> _pages = [];
    public long? Revision { get; private set; }
    public int ResidentGenres => _pages.Values.Sum(page => page.Items.Count);
    public event Action<bool>? Changed;
    [ObservableProperty] private IReadOnlyList<GenreRow> items = [];
    [ObservableProperty] private string search = "";
    [ObservableProperty] private int sortIndex = 1;
    [ObservableProperty] private bool descending = true;
    [ObservableProperty] private int total;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string error = "";
    public string SortLabel => Text[SortIndex switch { 0 => "genres.sortName", 2 => "genres.sortAlbums", _ => "genres.sortTracks" }];
    public bool IsNameSort => SortIndex == 0;
    public bool IsTrackSort => SortIndex == 1;
    public bool IsAlbumSort => SortIndex == 2;
    public double DirectionAngle => Descending ? 90 : 270;
    public string DirectionLabel => Text[Descending ? "query.desc" : "query.asc"];
    public string CountText => IsBusy && Revision is null ? Text["genres.loading"] : Text.Format("genres.count", Total);
    public string PageText => $"{_offset / 200 + 1} / {Math.Max(1, (Total + 199) / 200)}";
    public bool CanPrevious => !IsBusy && _offset > 0;
    public bool CanNext => !IsBusy && _offset + 200 < Total;
    public bool IsEmpty => !IsBusy && Items.Count == 0 && Error.Length == 0;
    partial void OnSearchChanged(string value) => _ = LoadAsync(0, true);
    partial void OnSortIndexChanged(int value) { NotifySort(); OnOptions(); }
    partial void OnDescendingChanged(bool value) { NotifySort(); OnOptions(); }
    partial void OnIsBusyChanged(bool value) => Notify();
    private void NotifySort()
    {
        OnPropertyChanged(nameof(SortLabel)); OnPropertyChanged(nameof(IsNameSort));
        OnPropertyChanged(nameof(IsTrackSort)); OnPropertyChanged(nameof(IsAlbumSort));
        OnPropertyChanged(nameof(DirectionAngle)); OnPropertyChanged(nameof(DirectionLabel));
    }
    private void Notify()
    {
        NotifySort(); OnPropertyChanged(nameof(CountText)); OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext)); OnPropertyChanged(nameof(IsEmpty)); Changed?.Invoke(false);
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
            var page = await library.GetGenresAsync(new(offset, Search: Search, Sort: (GenreSort)Math.Clamp(SortIndex, 0, 2),
                Descending: Descending), request.Token);
            if (_closed || generation != _generation) return;
            Items = page.Items; Total = page.Total; _offset = offset; Revision = page.Revision;
            _pages.Clear(); _pages[offset / 200] = page; Changed?.Invoke(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed && generation == _generation) Error = Text["genres.error"]; }
        finally { if (generation == _generation) { _request = null; IsBusy = false; Notify(); } }
    }
    public GenreRow? At(int index) => _pages.TryGetValue(index / 200, out var page) && index % 200 < page.Items.Count ? page.Items[index % 200] : null;
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
                var page = await library.GetGenresAsync(new(index * 200, Search: Search, Sort: (GenreSort)Math.Clamp(SortIndex, 0, 2),
                    Descending: Descending, Revision: Revision), request.Token);
                if (_closed || generation != _generation) return;
                while (_pages.Count >= 3) _pages.Remove(_pages.Keys.MaxBy(key => Math.Abs(key - first)));
                _pages[index] = page; Items = page.Items;
            }
            Changed?.Invoke(false);
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (!_closed && generation == _generation) await LoadAsync(); }
        catch (Exception) { if (!_closed && generation == _generation) Error = Text["genres.error"]; }
        finally { if (ReferenceEquals(_request, request)) _request = null; }
    }
    public void Dispose() { PreferenceChanged = null; Text.Changed -= Notify; _closed = true; ++_generation; _request?.Cancel(); _request = null; Items = []; _pages.Clear(); Changed = null; }
}
