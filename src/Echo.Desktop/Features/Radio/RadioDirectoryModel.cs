using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Application.Radio;
using Echo.Contracts.Radio;

namespace Echo.Desktop.Features.Radio;

public sealed partial class RadioDirectoryModel : ObservableObject, IDisposable
{
    private readonly RadioCatalog catalog;
    private readonly IRadioDirectory? directory;
    private readonly Action close;
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<RadioDirectoryStation> latest = [];
    private int epoch;
    private int offset;
    private bool disposed;

    public RadioDirectoryModel(RadioCatalog catalog, IRadioDirectory? directory, Action close)
    {
        this.catalog = catalog;
        this.directory = directory;
        this.close = close;
        catalog.Changed += CatalogChanged;
        Status = "输入条件后搜索，留空可浏览热门电台。";
    }

    public ObservableCollection<RadioDirectoryRow> Results { get; } = [];
    [ObservableProperty] private string queryName = "";
    [ObservableProperty] private string queryCountry = "";
    [ObservableProperty] private string queryLanguage = "";
    [ObservableProperty] private string queryTag = "";
    [ObservableProperty] private bool loading;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string limitMessage = "";
    [ObservableProperty] private bool hasPrevious;
    [ObservableProperty] private bool hasNext;
    [ObservableProperty] private string pageText = "";
    public bool HasResults => Results.Count > 0;

    [RelayCommand] private void Close() { Cancel(); close(); }
    [RelayCommand] private Task Search() { offset = 0; return LoadAsync(); }
    [RelayCommand] private Task Previous() { offset = Math.Max(0, offset - RadioDirectoryLimits.PageSize); return LoadAsync(); }
    [RelayCommand] private Task Next() { offset += RadioDirectoryLimits.PageSize; return LoadAsync(); }

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

    private async Task LoadAsync()
    {
        if (disposed) return;
        var request = ++epoch;
        Loading = true;
        LimitMessage = "";
        Status = "正在查询电台目录…";
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
        RadioDirectoryResult result;
        try
        {
            result = directory is null
                ? new([], false, "unavailable")
                : await directory.SearchAsync(new(QueryName, QueryCountry, QueryLanguage, QueryTag, offset), lifetime.Token);
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { return; }
        if (request != epoch || disposed) return;
        Loading = false;
        if (result.Error == "cancelled") return;
        latest = result.Stations;
        HasNext = result.HasMore;
        HasPrevious = offset > 0;
        PageText = result.Error is null ? $"第 {offset / RadioDirectoryLimits.PageSize + 1} 页" : "";
        Status = result.Error is not null ? "电台目录暂时不可用，请稍后重试。" : result.Stations.Count == 0 ? "没有找到电台" : "";
        Publish();
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
        directory?.Cancel();
        Loading = false;
        if (Results.Count == 0) Status = "输入条件后搜索，留空可浏览热门电台。";
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
