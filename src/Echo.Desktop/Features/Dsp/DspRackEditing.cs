using System.Text.Json.Nodes;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Playback;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    private JsonObject? rackWorkspace;
    [ObservableProperty] private Control? rackEditor;
    public IReadOnlyList<DspChoice> RackFilterKinds { get; } = [new("equalizer", "参数 EQ"), new("headphoneCorrection", "耳机校正"), new("procedural", "运算处理"), new("crossfeed", "交叉馈送"), new("convolution", "卷积")];
    [ObservableProperty] private DspChoice rackFilterKind = new("equalizer", "参数 EQ");
    public bool CanRemoveRackFilter => CanEdit && SelectedRack is { } row && FindRackFilter(row.Id) is not null;

    private void InitializeRack(JsonObject state)
    {
        rackWorkspace = (JsonObject)state.DeepClone();
        rackWorkspace["filters"] ??= new JsonArray();
        rackWorkspace["procedural"] ??= DspGraphTemplates.Procedural();
        var sequence = state["sequence"] as JsonArray ?? state["order"] as JsonArray;
        if (sequence is null || sequence.Count > 24) throw new IOException("无效的原生处理链。");
        rackOrder.AddRange(sequence.Select(x => x!.GetValue<string>()));
        RebuildRack();
        if (state["fixedPostStages"] is JsonArray stages)
            foreach (var stage in stages.Take(16))
            {
                var id = stage is JsonObject item ? item["id"]?.ToString() : stage?.ToString();
                if (id is not null) { RackRows.Add(new(id, "固定 · " + RackLabel(id), true)); RackStages.Add("固定 · " + id); }
            }
        SelectedRack = RackRows.FirstOrDefault();
    }

    private JsonObject? FindRackFilter(string id) => (rackWorkspace?["filters"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(x => x["id"]?.ToString() == id);

    private void RebuildRack()
    {
        RackRows.Clear(); RackStages.Clear();
        foreach (var id in rackOrder)
        {
            var filter = FindRackFilter(id);
            var label = filter is null ? RackLabel(id) : "滤波器 · " + (filter["name"]?.ToString() is { Length: > 0 } name ? name : RackLabel(filter["kind"]?.ToString() ?? id));
            RackRows.Add(new(id, label, false)); RackStages.Add(label);
        }
    }

    private void OpenRackSelection(DspRackRow? row)
    {
        RackEditor = null; OnPropertyChanged(nameof(CanRemoveRackFilter)); OnPropertyChanged(nameof(IsConvolutionRackFilter));
        if (row is null || row.Pinned || rackWorkspace is null) return;
        var state = FindRackFilter(row.Id) ?? (row.Id == "procedural" ? rackWorkspace["procedural"] as JsonObject : null);
        if (state is not null) RackEditor = DspGraphEditor.Create(state, RackEdited);
        else RackEditor = new TextBlock { Text = "在左侧对应模块中调整参数；这里编辑处理顺序。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    }

    private void RackEdited() { if (!projecting) IsDirty = true; }
    private void ReleaseRack() { RackEditor = null; rackWorkspace = null; }

    [RelayCommand] private void AddRackFilter()
    {
        if (!CanEdit || !IsRack || rackWorkspace?["filters"] is not JsonArray filters) return;
        if (filters.Count >= DspGraphTemplates.MaximumInstances) { Message = "最多添加 8 个滤波器实例。"; return; }
        if (RackFilterKind.Key == "convolution" && filters.OfType<JsonObject>().Count(x => x["kind"]?.ToString() == "convolution") >= 2) { Message = "最多添加 2 个卷积实例。"; return; }
        InsertRackFilter(DspGraphTemplates.Filter(RackFilterKind.Key), filters);
    }

    private void InsertRackFilter(JsonObject filter, JsonArray filters)
    {
        filters.Add((JsonNode)filter);
        var id = filter["id"]!.GetValue<string>();
        var index = SelectedRack is { Pinned: false } row ? rackOrder.IndexOf(row.Id) + 1 : rackOrder.Count;
        rackOrder.Insert(Math.Clamp(index, 0, rackOrder.Count), id);
        RebuildWithPinned(id); RackEdited();
    }

    [RelayCommand] private void DuplicateRackFilter()
    {
        if (!CanRemoveRackFilter || rackWorkspace?["filters"] is not JsonArray filters || SelectedRack is null) return;
        if (filters.Count >= DspGraphTemplates.MaximumInstances) { Message = "最多添加 8 个滤波器实例。"; return; }
        var copy = (JsonObject)FindRackFilter(SelectedRack.Id)!.DeepClone();
        if (copy["kind"]?.ToString() == "convolution" && filters.OfType<JsonObject>().Count(x => x["kind"]?.ToString() == "convolution") >= DspGraphTemplates.MaximumConvolvers)
        { Message = "最多添加 2 个卷积实例。"; return; }
        copy["id"] = "filter-" + Guid.NewGuid().ToString("N");
        InsertRackFilter(copy, filters);
    }

    [RelayCommand] private void RemoveRackFilter()
    {
        if (!CanRemoveRackFilter || SelectedRack is null || rackWorkspace?["filters"] is not JsonArray filters) return;
        var id = SelectedRack.Id; filters.Remove(FindRackFilter(id)); rackOrder.Remove(id);
        RebuildWithPinned(rackOrder.FirstOrDefault()); RackEdited();
    }

    private void RebuildWithPinned(string? selection)
    {
        var pinned = RackRows.Where(x => x.Pinned).ToArray();
        RebuildRack();
        foreach (var stage in pinned) { RackRows.Add(stage); RackStages.Add("固定 · " + stage.Id); }
        SelectedRack = RackRows.FirstOrDefault(x => x.Id == selection);
    }
}
