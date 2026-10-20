using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Application.Radio;
using Echo.Contracts.Radio;

namespace Echo.Desktop.Features.Radio;

public sealed partial class RadioDirectoryModel : ObservableObject, IDisposable
{
    private readonly IRadioCatalog catalog;
    private readonly IRadioDirectory? directory;
    private readonly Action close;
    private readonly Func<RadioStation, Task<bool>> play;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? pending;
    private IReadOnlyList<RadioDirectoryStation> latest = [];
    private RadioDirectoryQuery query = new("", "", "", "", 0);
    private int epoch;
    private bool disposed;

    public RadioDirectoryModel(IRadioCatalog catalog, IRadioDirectory? directory, Action close, Func<RadioStation, Task<bool>> play)
    {
        this.catalog = catalog;
        this.directory = directory;
        this.close = close;
        this.play = play;
        catalog.Changed += CatalogChanged;
        Status = "输入条件后搜索，留空可浏览热门电台。";
    }

    public ObservableCollection<RadioDirectoryRow> Results { get; } = [];
    [ObservableProperty] private string queryName = "";
    [ObservableProperty] private string queryCountry = "";
    [ObservableProperty] private string queryLanguage = "";
    [ObservableProperty] private string queryTag = "";
    [ObservableProperty] private bool loading;
    [ObservableProperty] private bool playing;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string limitMessage = "";
    [ObservableProperty] private bool hasPrevious;
    [ObservableProperty] private bool hasNext;
    [ObservableProperty] private string pageText = "";
    public bool HasResults => Results.Count > 0;
    public bool CanPrevious => HasPrevious && !Loading;
    public bool CanNext => HasNext && !Loading;
    partial void OnLoadingChanged(bool value) { OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext)); }
    partial void OnHasPreviousChanged(bool value) => OnPropertyChanged(nameof(CanPrevious));
    partial void OnHasNextChanged(bool value) => OnPropertyChanged(nameof(CanNext));

    [RelayCommand] private void Close() { Cancel(); close(); }
    public Task OpenAsync() => Search();
    [RelayCommand(AllowConcurrentExecutions = true)] private Task Search() => LoadAsync(new(QueryName, QueryCountry, QueryLanguage, QueryTag, 0));
    [RelayCommand] private Task Previous() => CanPrevious ? LoadAsync(query with { Offset = Math.Max(0, query.Offset - RadioDirectoryLimits.PageSize) }) : Task.CompletedTask;
    [RelayCommand] private Task Next() => CanNext ? LoadAsync(query with { Offset = query.Offset + RadioDirectoryLimits.PageSize }) : Task.CompletedTask;

    public async Task PlayAsync(RadioDirectoryRow row)
    {
        if (disposed || Playing) return;
        Playing = true;
        LimitMessage = "";
        try
        {
            var station = catalog.Stations.FirstOrDefault(item => item.Url == row.Station.Url) ?? RadioCatalog.FromDirectory(row.Station);
            if (!await play(station) && !disposed) LimitMessage = "电台连接失败，请重试或保存后编辑地址。";
            else if (!disposed && !catalog.Stations.Any(item => item.Url == station.Url))
                LimitMessage = catalog.Stations.Count >= RadioCatalog.Capacity
                    ? "已开始连接；电台列表已满，未自动保存。" : "收听请求已接受，但资料未保存，请重试保存。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) LimitMessage = error.Message; }
        finally { if (!disposed) Playing = false; }
    }

    public Task AddAsync(RadioDirectoryRow row)
    {
        if (disposed || row.Saved) return Task.CompletedTask;
        return catalog.UpdateAsync(items =>
        {
            if (items.Any(item => item.Url == row.Station.Url)) return items;
            if (items.Count >= RadioCatalog.Capacity) throw new ArgumentException("已达到 200 个电台的保存上限，请先移除一些电台。");
            return new[] { RadioCatalog.FromDirectory(row.Station) }.Concat(items);
        }, lifetime.Token);
    }

    private async Task LoadAsync(RadioDirectoryQuery next)
    {
        if (disposed) return;
        pending?.Cancel();
        var mine = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        pending = mine;
        var request = ++epoch;
        query = next;
        Loading = true;
        LimitMessage = "";
        Status = "正在查询电台目录…";
        Results.Clear();
        latest = [];
        HasPrevious = HasNext = false;
        PageText = "";
        OnPropertyChanged(nameof(HasResults));
        try
        {
            var result = directory is null
                ? new([], false, "unavailable")
                : await directory.SearchAsync(next, mine.Token);
            if (request != epoch || disposed || mine.IsCancellationRequested) return;
            latest = result.Stations;
            HasNext = result.HasMore && next.Offset < RadioDirectoryLimits.MaxOffset;
            HasPrevious = next.Offset > 0;
            PageText = result.Error is null || HasPrevious ? $"第 {next.Offset / RadioDirectoryLimits.PageSize + 1} 页" : "";
            Status = result.Error == "busy" ? "电台目录繁忙，请稍后重试。"
                : result.Error is not null ? "电台目录暂时不可用，请重试。" : result.Stations.Count == 0 ? "没有找到电台" : "";
            Publish();
        }
        catch (OperationCanceledException) when (mine.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (request == epoch && !disposed) Status = "查询失败：" + error.Message;
        }
        finally
        {
            if (request == epoch && !disposed) Loading = false;
            if (ReferenceEquals(pending, mine)) pending = null;
            mine.Dispose();
        }
    }

    private void CatalogChanged() => Dispatcher.UIThread.Post(() => { if (!disposed) Publish(); });

    private void Publish()
    {
        var saved = catalog.Stations.Select(station => station.Url).ToHashSet(StringComparer.Ordinal);
        Results.Clear();
        foreach (var station in latest) Results.Add(new(station, saved.Contains(station.Url)));
        OnPropertyChanged(nameof(HasResults));
    }

    public void Cancel()
    {
        epoch++;
        pending?.Cancel();
        Loading = false;
        latest = [];
        Results.Clear();
        HasPrevious = HasNext = false;
        PageText = LimitMessage = "";
        Status = "输入条件后搜索，留空可浏览热门电台。";
        OnPropertyChanged(nameof(HasResults));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        catalog.Changed -= CatalogChanged;
        Cancel();
        lifetime.Cancel();
        lifetime.Dispose();
        Results.Clear();
    }
}

public sealed class RadioDirectoryRow(RadioDirectoryStation station, bool saved)
{
    public RadioDirectoryStation Station { get; } = station;
    public bool Saved { get; } = saved;
    public string Name => Station.Name;
    public string Detail => string.Join(" · ", new[] { Station.CountryCode, Station.Language, Station.Tags, Station.Codec, Station.Bitrate > 0 ? Station.Bitrate + " kbps" : "" }.Where(item => item.Length > 0));
    public string Action => Saved ? "已添加" : "保存";
}
