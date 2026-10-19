using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Core.Contracts;

namespace Echo.Desktop.Features.Dsp;

public sealed record DspImpulseBankChoice(string Id, string Name, int Filters)
{ public string Label => $"{Name} · {Filters} 个脉冲"; }

public sealed partial class DspViewModel
{
    public ObservableCollection<DspImpulseBankChoice> ImpulseBanks { get; } = [];
    [ObservableProperty] private DspImpulseBankChoice? selectedImpulseBank;
    public bool IsConvolutionRackFilter => IsRack && SelectedRack is { } row && FindRackFilter(row.Id)?["kind"]?.ToString() == "convolution";
    [RelayCommand] private async Task RefreshBanksAsync()
    {
        if (!CanUseResources) return; ResourceBusy = true;
        try { await ReadBanksAsync(); Message = ImpulseBanks.Count == 0 ? "还没有导入卷积资源。" : "卷积资源已刷新。"; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed) Message = "资源读取失败：" + error.Message; }
        finally { if (!disposed) ResourceBusy = false; }
    }
    [RelayCommand] private async Task DeleteBankAsync()
    {
        if (!CanUseResources || SelectedImpulseBank is not { } bank) return;
        ResourceBusy = true;
        try
        {
            await resources.ExecuteAsync(DspResourceOperation.DeleteBank, id: bank.Id, token: lifetime.Token);
            await ReadBanksAsync();
            Message = "资源包已删除；已有方案再次应用时需选择有效资源。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed) Message = "删除失败：" + error.Message; }
        finally { if (!disposed) ResourceBusy = false; }
    }
    private async Task ReadBanksAsync()
    {
        var version = generation;
        var result = await resources.ExecuteAsync(DspResourceOperation.Banks, token: lifetime.Token);
        if (disposed || generation != version) return;
        projecting = true;
        try
        {
            var selectedId = IsRoom ? confirmed?["irId"]?.ToString() : SelectedRack is { } row ? FindRackFilter(row.Id)?["convolution"]?["bankId"]?.ToString() : null;
            ImpulseBanks.Clear(); SelectedImpulseBank = null;
            foreach (var item in result.EnumerateArray().Take(32))
                ImpulseBanks.Add(new(item.GetProperty("id").GetString()!, item.GetProperty("name").GetString()!, item.GetProperty("filterCount").GetInt32()));
            SelectedImpulseBank = ImpulseBanks.FirstOrDefault(x => x.Id == selectedId);
        }
        finally { projecting = false; }
    }
    public async Task ImportImpulseBankAsync(string path)
    {
        if (!CanUseResources || (!IsRoom && !IsConvolutionRackFilter)) return;
        ResourceBusy = true; var version = generation; var rowId = SelectedRack?.Id;
        try
        {
            var imported = await resources.ExecuteAsync(DspResourceOperation.ImportImpulse, path: path, token: lifetime.Token);
            if (disposed || generation != version || IsRack && SelectedRack?.Id != rowId) return;
            await ReadBanksAsync();
            var selected = ImpulseBanks.Single(x => x.Id == imported.GetProperty("id").GetString());
            projecting = true; try { SelectedImpulseBank = selected; } finally { projecting = false; }
            await SelectImpulseBankAsync(selected, version, rowId);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed && generation == version) Message = "脉冲导入失败：" + error.Message; }
        finally { if (!disposed) ResourceBusy = false; }
    }
    partial void OnSelectedImpulseBankChanged(DspImpulseBankChoice? value)
    { if (!projecting && value is not null && CanUseResources) _ = SelectImpulseFromUiAsync(value); }
    private async Task SelectImpulseFromUiAsync(DspImpulseBankChoice value)
    {
        ResourceBusy = true;
        try { await SelectImpulseBankAsync(value, generation, SelectedRack?.Id); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed) Message = "选择脉冲失败：" + error.Message; }
        finally { if (!disposed) ResourceBusy = false; }
    }
    private async Task SelectImpulseBankAsync(DspImpulseBankChoice selected, int version, string? rowId)
    {
        var result = await resources.ExecuteAsync(DspResourceOperation.ResolveBank, id: selected.Id, token: lifetime.Token);
        if (disposed || version != generation) return;
        if (IsConvolutionRackFilter && SelectedRack?.Id == rowId)
        {
            var convolution = FindRackFilter(rowId!)!["convolution"]!.AsObject();
            convolution["bankId"] = selected.Id; convolution["irName"] = selected.Name; convolution["nativePath"] = result.GetProperty("nativePath").GetString();
            RackEdited(); Message = "脉冲已加入机架草稿，应用处理链后生效。";
        }
        else if (IsRoom)
        {
            IrPath = result.GetProperty("nativePath").GetString()!; RoomFacts = selected.Label;
            IsDirty = true; NoteEdit(); Message = "脉冲已准备，启用房间校正后可应用。";
        }
    }
    private async Task ResolveRackBanksAsync(JsonObject state)
    {
        if (state["filters"] is not JsonArray filters) return;
        foreach (var filter in filters.OfType<JsonObject>())
        {
            if (filter["convolution"] is not JsonObject convolution || convolution["bankId"]?.ToString() is not { Length: > 0 } id) continue;
            var result = await resources.ExecuteAsync(DspResourceOperation.ResolveBank, id: id, token: lifetime.Token);
            convolution["nativePath"] = result.GetProperty("nativePath").GetString();
        }
    }
}
