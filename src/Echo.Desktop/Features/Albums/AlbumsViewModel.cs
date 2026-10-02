using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Albums;

public sealed partial class AlbumsViewModel(IAlbumLibrary library) : ObservableObject, IDisposable
{
    public const int PageSize = 200;
    private readonly Dictionary<int, AlbumPage> _pages = [];
    private CancellationTokenSource? _request;
    private int _generation;
    private bool _closed;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private int sortIndex;
    [ObservableProperty] private bool descending;
    [ObservableProperty] private bool losslessOnly;
    [ObservableProperty] private int total;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private bool isLoading;
    public long? Revision { get; private set; }
    public int ResidentAlbums => _pages.Values.Sum(page => page.Items.Count);
    public string CountText => IsLoading && Revision is null ? "正在读取专辑…" : $"共 {Total:N0} 张专辑";
    public bool IsEmpty => Revision is not null && Total == 0 && !IsLoading && Error.Length == 0;
    public event Action<bool>? Changed;

    partial void OnSearchChanged(string value) => _ = RefreshAsync(debounce: true);
    partial void OnSortIndexChanged(int value) => _ = RefreshAsync();
    partial void OnDescendingChanged(bool value) => _ = RefreshAsync();
    partial void OnLosslessOnlyChanged(bool value) => _ = RefreshAsync();
    partial void OnTotalChanged(int value) => NotifyStatus();
    partial void OnIsLoadingChanged(bool value) => NotifyStatus();
    partial void OnErrorChanged(string value) => NotifyStatus();
    private void NotifyStatus() { OnPropertyChanged(nameof(CountText)); OnPropertyChanged(nameof(IsEmpty)); }

    private AlbumQuery Query(int offset, long? revision) => new(offset, PageSize,
        Search, (AlbumSort)Math.Clamp(SortIndex, 0, 5), Descending, LosslessOnly, revision);

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
        IsLoading = true; Error = "";
        try
        {
            if (debounce) await Task.Delay(120, request.Token);
            var page = await library.GetAlbumsAsync(Query(0, null), request.Token);
            if (_closed || generation != _generation) return;
            _pages.Clear(); _pages[0] = page;
            Revision = page.Revision; Total = page.Total;
            IsLoading = false;
            Changed?.Invoke(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        { if (generation == _generation && !_closed) Error = "专辑读取失败。请确认曲库可用，再点击刷新。"; }
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
                var page = await library.GetAlbumsAsync(Query(pageIndex * PageSize, Revision), request.Token);
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
        { if (generation == _generation && !_closed) Error = "这一页读取失败，点击刷新重试。"; }
        finally { if (generation == _generation) _request = null; request.Dispose(); }
    }

    public void UpdatePersonalState(string id, bool favorite, int rating)
    {
        foreach (var key in _pages.Keys.ToArray())
        {
            var page = _pages[key];
            if (page.Items.Any(item => item.Id == id))
                _pages[key] = page with { Items = page.Items.Select(item => item.Id == id ? item with { Favorite = favorite, Rating = rating } : item).ToArray() };
        }
        Changed?.Invoke(false);
    }

    public void Dispose()
    { _closed = true; ++_generation; _request?.Cancel(); _request = null; _pages.Clear(); Changed = null; }
}
