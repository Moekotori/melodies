using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Core.Contracts;

namespace Echo.Desktop.Features.Dsp;

public sealed record OpraPresetChoice(string Id, string Vendor, string Product, string Author, string Details = "")
{ public string Label => Author; }
public sealed record OpraBrowserItem(string Id, string Name, string Detail, int Count)
{ public string Summary => string.IsNullOrEmpty(Detail) ? $"{Count} 款耳机" : $"{Detail} · {Count} 条曲线"; }

public sealed partial class DspViewModel
{
    public ObservableCollection<OpraPresetChoice> HeadphoneResults { get; } = [];
    public ObservableCollection<OpraBrowserItem> HeadphoneBrowseItems { get; } = [];
    public bool HasHeadphoneResults => HeadphoneResults.Count > 0;
    public string HeadphoneTitle => headphoneProduct?.Name ?? activeHeadphoneProfile?["product"]?.ToString() ?? "选择耳机";
    public string HeadphoneVendor => headphoneProduct?.Detail ?? activeHeadphoneProfile?["vendor"]?.ToString() ?? "OPRA by Roon";
    public IReadOnlyList<DspChoice> CorrectionModes { get; } = [new("original", "作者原始曲线"), new("listening", "听感调整")];
    [ObservableProperty] private string headphoneQuery = "";
    [ObservableProperty] private string headphoneAttribution = "按生产商品牌浏览，或直接搜索耳机型号。";
    [ObservableProperty] private string headphoneBrowseTitle = "所有生产商";
    [ObservableProperty] private string headphoneEmptyText = "正在读取本地目录…";
    [ObservableProperty] private string headphoneLibraryStatus = "OPRA · 开放耳机校正目录";
    [ObservableProperty] private OpraPresetChoice? selectedHeadphone;
    [ObservableProperty] private OpraBrowserItem? selectedHeadphoneItem;
    [ObservableProperty] private DspChoice correctionMode = new("original", "作者原始曲线");
    [ObservableProperty] private bool headphoneHasMore;
    private string headphoneBrowseKind = "vendors";
    private string? headphoneVendorId;
    private OpraBrowserItem? headphoneProduct;
    private int headphoneOffset, headphoneBrowseEpoch;
    private int headphonePresetOffset;
    private bool headphonePresetMore;
    public bool CanNextHeadphoneSources => headphonePresetMore && CanUseResources;
    public bool CanPreviousHeadphoneSources => headphonePresetOffset > 0 && CanUseResources;
    public string HeadphoneSourcePage => $"{headphonePresetOffset / 30 + 1}";
    private JsonObject? headphonePreview;
    private bool updatingHeadphoneBrowser;
    private readonly HashSet<string> headphoneFavorites = new(StringComparer.Ordinal);
    public bool CanLoadHeadphone => CanUseResources && headphonePreview is not null;
    public bool HasPreviousHeadphones => headphoneOffset > 0 && !ResourceBusy;
    public bool CanNextHeadphones => HeadphoneHasMore && !ResourceBusy;
    public bool CanGoBackHeadphones => headphoneBrowseKind == "products" && headphoneVendorId is not null && !ResourceBusy;
    public bool HeadphoneIsFavorite => headphoneProduct is { } p && headphoneFavorites.Contains(p.Id);
    public string HeadphoneFavoriteLabel => HeadphoneIsFavorite ? "已收藏 · 取消" : "收藏型号";
    public bool CanFavoriteHeadphone => CanUseResources && headphoneProduct is not null;

    private async Task InitializeHeadphonesAsync()
    {
        var version = generation;
        var library = await ReadLibraryAsync();
        if (disposed || version != generation || !IsHeadphone) return;
        headphoneFavorites.Clear();
        if (library["opraFavorites"] is JsonArray favorites)
            foreach (var id in favorites.Take(100)) if (id is not null) headphoneFavorites.Add(id.ToString());
        NotifyHeadphoneBrowser();
        await QueryHeadphonesAsync();
    }

    [RelayCommand] private async Task BrowseHeadphonesAsync(string kind)
    {
        if (!CanUseResources) return;
        headphoneBrowseKind = kind is "favorites" or "recent" ? kind : "vendors";
        headphoneVendorId = null; headphoneOffset = 0; HeadphoneQuery = "";
        HeadphoneBrowseTitle = kind switch { "favorites" => "收藏型号", "recent" => "最近使用", _ => "所有生产商" };
        await QueryHeadphonesAsync();
    }
    [RelayCommand] private async Task SearchHeadphonesAsync()
    {
        if (!CanUseResources) return;
        headphoneBrowseKind = "products"; headphoneVendorId = null; headphoneOffset = 0;
        HeadphoneBrowseTitle = string.IsNullOrWhiteSpace(HeadphoneQuery) ? "全部耳机" : "搜索结果";
        await QueryHeadphonesAsync();
    }
    [RelayCommand] private async Task NextHeadphonesAsync() { if (CanNextHeadphones) { headphoneOffset += 30; await QueryHeadphonesAsync(); } }
    [RelayCommand] private async Task PreviousHeadphonesAsync() { if (HasPreviousHeadphones) { headphoneOffset -= 30; await QueryHeadphonesAsync(); } }
    [RelayCommand] private async Task RefreshHeadphonesAsync() { if (CanUseResources) { headphoneOffset = 0; await QueryHeadphonesAsync(true); } }
    [RelayCommand] private async Task BackHeadphonesAsync() => await BrowseHeadphonesAsync("vendors");

    private async Task QueryHeadphonesAsync(bool refresh = false)
    {
        if (!CanUseResources || !IsHeadphone) return;
        ResourceBusy = true; var version = generation; var epoch = ++headphoneBrowseEpoch;
        try
        {
            var result = await resources.ExecuteAsync(DspResourceOperation.BrowseOpra, name: headphoneBrowseKind,
                id: headphoneVendorId, query: HeadphoneQuery, offset: headphoneOffset, refresh: refresh, token: lifetime.Token);
            if (disposed || version != generation || epoch != headphoneBrowseEpoch) return;
            updatingHeadphoneBrowser = true;
            try
            {
                SelectedHeadphoneItem = null; HeadphoneBrowseItems.Clear();
                foreach (var node in result.GetProperty("items").EnumerateArray())
                    HeadphoneBrowseItems.Add(new(node.GetProperty("id").GetString()!, node.GetProperty("name").GetString()!,
                        node.GetProperty("detail").GetString()!, node.GetProperty("count").GetInt32()));
            }
            finally { updatingHeadphoneBrowser = false; }
            HeadphoneHasMore = result.GetProperty("hasMore").GetBoolean();
            var cached = result.GetProperty("cached").GetBoolean();
            HeadphoneEmptyText = !cached ? "还没有本地目录，点击「刷新库」获取品牌与型号。"
                : headphoneBrowseKind == "favorites" ? "还没有收藏耳机型号。" : headphoneBrowseKind == "recent" ? "应用校正后，型号会显示在这里。" : "没有匹配的耳机。";
            HeadphoneLibraryStatus = !cached ? "OPRA · 未缓存" : refresh ? "OPRA · 刚刚更新" : "OPRA · 本地缓存";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed && generation == version) Message = "OPRA 目录读取失败：" + error.Message; }
        finally { if (!disposed && epoch == headphoneBrowseEpoch) { ResourceBusy = false; NotifyHeadphoneBrowser(); } }
    }

    partial void OnSelectedHeadphoneItemChanged(OpraBrowserItem? value)
    { if (!updatingHeadphoneBrowser && value is not null && CanUseResources) _ = OpenHeadphoneItemAsync(value); }
    [RelayCommand] private async Task NextHeadphoneSourcesAsync() { if (CanNextHeadphoneSources && headphoneProduct is { } product) await OpenHeadphoneItemAsync(product, headphonePresetOffset + 30); }
    [RelayCommand] private async Task PreviousHeadphoneSourcesAsync() { if (CanPreviousHeadphoneSources && headphoneProduct is { } product) await OpenHeadphoneItemAsync(product, headphonePresetOffset - 30); }
    private async Task OpenHeadphoneItemAsync(OpraBrowserItem item, int sourceOffset = 0)
    {
        if (headphoneBrowseKind == "vendors")
        {
            headphoneBrowseKind = "products"; headphoneVendorId = item.Id; headphoneOffset = 0; HeadphoneQuery = "";
            HeadphoneBrowseTitle = item.Name; await QueryHeadphonesAsync(); return;
        }
        headphoneProduct = item; headphonePreview = null; headphonePresetOffset = sourceOffset;
        ResourceBusy = true; var version = generation; var epoch = ++headphoneBrowseEpoch;
        try
        {
            SelectedHeadphone = null; HeadphoneResults.Clear();
            NotifyHeadphonePreview(); NotifyHeadphoneBrowser();
            var result = await resources.ExecuteAsync(DspResourceOperation.BrowseOpra, name: "presets", id: item.Id, offset: sourceOffset, token: lifetime.Token);
            if (disposed || generation != version || epoch != headphoneBrowseEpoch) return;
            foreach (var node in result.GetProperty("items").EnumerateArray())
                HeadphoneResults.Add(new(node.GetProperty("id").GetString()!, item.Detail, item.Name, node.GetProperty("name").GetString()!, node.GetProperty("detail").GetString()!));
            headphonePresetMore = result.GetProperty("hasMore").GetBoolean();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed && generation == version) Message = "校正来源读取失败：" + error.Message; }
        finally { if (!disposed && epoch == headphoneBrowseEpoch) { ResourceBusy = false; NotifyHeadphoneBrowser(); } }
        if (!disposed && generation == version && epoch == headphoneBrowseEpoch) SelectedHeadphone = HeadphoneResults.FirstOrDefault();
    }

    partial void OnSelectedHeadphoneChanged(OpraPresetChoice? value)
    { if (!projecting && value is not null) _ = PreviewHeadphoneAsync(value); }
    private async Task PreviewHeadphoneAsync(OpraPresetChoice selected)
    {
        var version = generation; var epoch = ++headphoneBrowseEpoch;
        ResourceBusy = true; headphonePreview = null; RevokeHeadphoneGainConsent(); NotifyHeadphonePreview();
        try
        {
            var result = await resources.ExecuteAsync(DspResourceOperation.OpraPreset, id: selected.Id, token: lifetime.Token);
            if (disposed || generation != version || epoch != headphoneBrowseEpoch || SelectedHeadphone != selected) return;
            headphonePreview = JsonNode.Parse(result.GetRawText())!.AsObject();
            projecting = true;
            try { CorrectionMode = CorrectionModes.First(x => x.Key == "original"); } finally { projecting = false; }
            HeadphoneAttribution = $"作者：{selected.Author} · OPRA · CC-BY-SA-4.0\n{headphonePreview["details"]}\n{headphonePreview["link"]}";
            Message = "曲线已预览；载入试听后可应用到独立耳机校正链。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed && generation == version) Message = "此预设不能应用：" + error.Message; }
        finally { if (!disposed && epoch == headphoneBrowseEpoch) { ResourceBusy = false; NotifyHeadphonePreview(); } }
    }

    [RelayCommand] private async Task ToggleHeadphoneFavoriteAsync()
    {
        if (!CanFavoriteHeadphone || headphoneProduct is not { } product) return;
        ResourceBusy = true;
        try
        {
            var enabled = !HeadphoneIsFavorite;
            await resources.ExecuteAsync(DspResourceOperation.FavoriteOpra, id: product.Id, state: Json(new JsonObject { ["enabled"] = enabled }), token: lifetime.Token);
            if (enabled) headphoneFavorites.Add(product.Id); else headphoneFavorites.Remove(product.Id);
            Message = enabled ? "已收藏 " + product.Name : "已取消收藏 " + product.Name;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed) Message = "收藏更新失败：" + error.Message; }
        finally { if (!disposed) { ResourceBusy = false; NotifyHeadphoneBrowser(); } }
    }

    private void NotifyHeadphoneBrowser()
    {
        foreach (var name in new[] { nameof(HeadphoneTitle), nameof(HeadphoneVendor), nameof(HasHeadphoneResults), nameof(HasPreviousHeadphones),
            nameof(CanNextHeadphones), nameof(CanGoBackHeadphones), nameof(CanFavoriteHeadphone), nameof(HeadphoneFavoriteLabel), nameof(HeadphoneIsFavorite),
            nameof(CanNextHeadphoneSources), nameof(CanPreviousHeadphoneSources), nameof(HeadphoneSourcePage) }) OnPropertyChanged(name);
    }
}
