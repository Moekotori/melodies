using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Library;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Albums;

public sealed partial class AlbumsViewModel : ObservableObject, IDisposable
{
    private readonly IAlbumLibrary library;
    private readonly ArtistRow? artist;
    private readonly string? genreId;
    private readonly LocalizationService Text;
    public AlbumsViewModel(IAlbumLibrary library, ArtistRow? artist = null, LocalizationService? text = null, LibraryViewPreferences? preference = null, string? genreId = null)
    { this.library = library; this.artist = artist; this.genreId = genreId; Filters.AttachLibrary(library);
        if (preference is not null) { sortIndex = Math.Clamp(preference.SortIndex,0,11); descending = preference.Descending; Filters.Restore(preference.Filter); }
        Text = text ?? new(); Text.Changed += OnLanguageChanged; Filters.Changed += OnFiltersChanged; RebuildSortOptions(); }
    public LibraryFilterViewModel Filters { get; } = new();
    public event Action? PreferenceChanged;
    public LibraryViewPreferences Preference => new("albums", SortIndex, Descending, Filter: Filters.Snapshot() with { LosslessOnly = LosslessOnly || Filters.LosslessOnly });
    private void OnFiltersChanged() { PreferenceChanged?.Invoke(); _ = RefreshAsync(debounce: true); }
    private void OnOptions() { PreferenceChanged?.Invoke(); _ = RefreshAsync(); }
    public const int PageSize = 200;
    private readonly Dictionary<int, AlbumPage> _pages = [];
    private CancellationTokenSource? _request;
    private int _generation;
    private bool _closed;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private int sortIndex = (int)AlbumSort.Default;
    [ObservableProperty] private bool descending;
    [ObservableProperty] private bool losslessOnly;
    [ObservableProperty] private int total;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private bool isLoading;
    public long? Revision { get; private set; }
    public int ResidentAlbums => _pages.Values.Sum(page => page.Items.Count);
    public string CountText => IsLoading && Revision is null ? Text.Translate("正在读取专辑…") : Text.Format("albums.count", Total);
    public bool IsEmpty => Revision is not null && Total == 0 && !IsLoading && Error.Length == 0;
    public event Action<bool>? Changed;

    partial void OnSearchChanged(string value) => _ = RefreshAsync(debounce: true);
    partial void OnSortIndexChanged(int value) { NotifySort(); if (!changingSort) OnOptions(); }
    partial void OnDescendingChanged(bool value) { NotifySort(); if (!changingSort) OnOptions(); }
    partial void OnLosslessOnlyChanged(bool value) => OnOptions();
    partial void OnTotalChanged(int value) => NotifyStatus();
    partial void OnIsLoadingChanged(bool value) => NotifyStatus();
    partial void OnErrorChanged(string value) => NotifyStatus();
    private void NotifyStatus() { OnPropertyChanged(nameof(CountText)); OnPropertyChanged(nameof(IsEmpty)); }

    private AlbumQuery Query(int offset, long? revision) => new(offset, PageSize,
        Search, (AlbumSort)Math.Clamp(SortIndex, 0, 11), Descending, LosslessOnly, revision, Filters.Snapshot(), shufflePivot);

    private Task<AlbumPage> ReadAsync(AlbumQuery query, CancellationToken token) => artist is not null && library is IArtistLibrary artists
        ? artists.GetArtistAlbumsAsync(artist.Id, artist.Grouping, query, token)
        : genreId is not null && library is IGenreLibrary genres ? genres.GetGenreAlbumsAsync(genreId, query, token) : library.GetAlbumsAsync(query, token);

    private (CancellationTokenSource Request, int Generation) Start()
    {
        _request?.Cancel();
        var request = new CancellationTokenSource();
        _request = request;
        return (request, ++_generation);
    }

    public async Task RefreshAsync(bool debounce = false)
    {
        if (_closed) return;
        var (request, generation) = Start();
        _pages.Clear(); Revision = null; Total = 0; Changed?.Invoke(true);
        IsLoading = true; Error = "";
        try
        {
            if (debounce) await Task.Delay(120, request.Token);
            var page = await ReadAsync(Query(0, null), request.Token);
            if (_closed || generation != _generation) return;
            _pages.Clear(); _pages[0] = page;
            Revision = page.Revision; Total = page.Total;
            IsLoading = false;
            Changed?.Invoke(true);
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException)
        { if (generation == _generation && !_closed) Error = Text["query.invalid"]; }
        catch (Exception)
        { if (generation == _generation && !_closed) Error = Text.Translate("专辑读取失败。请确认曲库可用，再点击刷新。"); }
        finally
        {
            if (generation == _generation) { IsLoading = false; _request = null; }
            request.Dispose();
        }
    }

    public AlbumRow? At(int index)
        => _pages.TryGetValue(index / PageSize, out var page) && index % PageSize < page.Items.Count
            ? page.Items[index % PageSize] : null;

    public async Task LoadRangeAsync(int start, int count)
    {
        if (_closed || IsLoading || Revision is null || Total == 0) return;
        var first = start / PageSize;
        var last = Math.Min((Total - 1) / PageSize, (start + count - 1) / PageSize);
        if (Enumerable.Range(first, last - first + 1).All(_pages.ContainsKey)) return;
        var (request, generation) = Start();
        try
        {
            for (var pageIndex = first; pageIndex <= last; pageIndex++)
            {
                if (_pages.ContainsKey(pageIndex)) continue;
                var page = await ReadAsync(Query(pageIndex * PageSize, Revision), request.Token);
                if (_closed || generation != _generation) return;
                while (_pages.Count >= 3)
                {
                    var victim = _pages.Keys.MaxBy(key => Math.Abs(key - first));
                    _pages.Remove(victim);
                }
                _pages[pageIndex] = page;
            }
            Error = ""; Changed?.Invoke(false);
        }
        catch (OperationCanceledException) { }
        catch (LibraryChangedException) { if (generation == _generation) await RefreshAsync(); }
        catch (Exception)
        { if (generation == _generation && !_closed) Error = Text.Translate("这一页读取失败，点击刷新重试。"); }
        finally { if (generation == _generation) _request = null; request.Dispose(); }
    }

    public void UpdatePersonalState(string id, bool favorite, int rating)
    {
        if (Filters.FavoriteOnly && !favorite) { _ = RefreshAsync(); return; }
        foreach (var key in _pages.Keys.ToArray())
        {
            var page = _pages[key];
            if (page.Items.Any(item => item.Id == id))
                _pages[key] = page with { Items = page.Items.Select(item => item.Id == id ? item with { Favorite = favorite, Rating = rating } : item).ToArray() };
        }
        Changed?.Invoke(false);
    }

    public void Dispose()
    { PreferenceChanged = null; Filters.Changed -= OnFiltersChanged; Text.Changed -= OnLanguageChanged; _closed = true; ++_generation; _request?.Cancel(); _request = null; _pages.Clear(); Changed = null; }
}
