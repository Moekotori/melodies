using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Core.Contracts;

namespace Echo.Desktop.Features.Dsp;

public sealed record OpraPresetChoice(string Id, string Vendor, string Product, string Author, string Details = "")
{ public string Label => Vendor + " · " + Product; }

public sealed partial class DspViewModel
{
    public ObservableCollection<OpraPresetChoice> HeadphoneResults { get; } = [];
    public IReadOnlyList<DspChoice> CorrectionModes { get; } = [new("original", "作者原始参数"), new("listening", "听感调整")];
    [ObservableProperty] private string headphoneQuery = "";
    [ObservableProperty] private string headphoneAttribution = "搜索 OPRA 品牌、型号或作者，选择后可预览参数来源。";
    [ObservableProperty] private OpraPresetChoice? selectedHeadphone;
    [ObservableProperty] private DspChoice correctionMode = new("original", "作者原始参数");
    [ObservableProperty] private bool headphoneHasMore;
    private int headphoneOffset;
    private JsonObject? headphonePreview;
    private bool correctionLoaded;
    public bool CanLoadHeadphone => CanUseResources && headphonePreview is not null;
    public bool HasPreviousHeadphones => headphoneOffset > 0 && !ResourceBusy;
    [RelayCommand] private async Task SearchHeadphonesAsync() { headphoneOffset = 0; await QueryHeadphonesAsync(); }
    [RelayCommand] private async Task NextHeadphonesAsync() { if (HeadphoneHasMore) { headphoneOffset += 30; await QueryHeadphonesAsync(); } }
    [RelayCommand] private async Task PreviousHeadphonesAsync() { if (headphoneOffset > 0) { headphoneOffset = Math.Max(0, headphoneOffset - 30); await QueryHeadphonesAsync(); } }
    [RelayCommand] private async Task RefreshHeadphonesAsync() { headphoneOffset = 0; await QueryHeadphonesAsync(true); }
    private async Task QueryHeadphonesAsync(bool refresh = false)
    {
        if (!CanUseResources || !IsHeadphone) return;
        ResourceBusy = true; var version = generation;
        try
        {
            var result = await resources.ExecuteAsync(DspResourceOperation.SearchOpra, query: HeadphoneQuery, offset: headphoneOffset, refresh: refresh, token: lifetime.Token);
            if (disposed || version != generation) return;
            HeadphoneResults.Clear(); SelectedHeadphone = null;
            foreach (var node in result.GetProperty("items").EnumerateArray())
                HeadphoneResults.Add(new(node.GetProperty("id").GetString()!, node.GetProperty("vendor").GetString()!, node.GetProperty("product").GetString()!, node.GetProperty("author").GetString()!, node.TryGetProperty("details", out var details) ? details.GetString() ?? "" : ""));
            HeadphoneHasMore = result.GetProperty("hasMore").GetBoolean();
            Message = HeadphoneResults.Count == 0 ? "没有匹配的耳机或参数预设。" : "选择预设以查看作者与来源。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed && generation == version) Message = "OPRA 搜索失败：" + error.Message; }
        finally { if (!disposed) { ResourceBusy = false; OnPropertyChanged(nameof(HasPreviousHeadphones)); } }
    }
    partial void OnSelectedHeadphoneChanged(OpraPresetChoice? value) { if (value is not null) _ = PreviewHeadphoneAsync(value); }
    private async Task PreviewHeadphoneAsync(OpraPresetChoice selected)
    {
        var version = generation; ResourceBusy = true; correctionLoaded = false; headphonePreview = null; OnPropertyChanged(nameof(CanLoadHeadphone));
        try
        {
            var result = await resources.ExecuteAsync(DspResourceOperation.OpraPreset, id: selected.Id, token: lifetime.Token);
            if (disposed || generation != version || SelectedHeadphone != selected) return;
            headphonePreview = JsonNode.Parse(result.GetRawText())!.AsObject();
            HeadphoneAttribution = $"{selected.Vendor} / {selected.Product}\n作者：{selected.Author} · OPRA · CC-BY-SA-4.0\n{headphonePreview["details"]}\n{headphonePreview["link"]}";
            Message = "作者参数已读取。载入并试听后，可应用到独立耳机校正链。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed && generation == version) Message = "此预设不能应用：" + error.Message; }
        finally { if (!disposed) { ResourceBusy = false; OnPropertyChanged(nameof(CanLoadHeadphone)); } }
    }
    [RelayCommand] private void LoadHeadphone()
    {
        if (!CanLoadHeadphone || !IsHeadphone || headphonePreview?[CorrectionMode.Key] is not JsonObject preset) return;
        correctionLoaded = true; UsePresetDraft(preset); Enabled = true;
        Message = "正在试听独立耳机校正；点击应用保留调整。";
    }
    partial void OnCorrectionModeChanged(DspChoice value) { if (correctionLoaded) LoadHeadphone(); }
}
