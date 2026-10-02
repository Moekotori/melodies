using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed record EqPresetChoice(string Label, JsonObject? State);

public sealed partial class DspViewModel
{
    public ObservableCollection<EqPresetChoice> Presets { get; } = [];
    [ObservableProperty] private EqPresetChoice? selectedPreset;

    private async Task ReadPresetsAsync(int version)
    {
        try
        {
            var result = await session!.DspAsync("preset.list", Json(new JsonArray()), token: lifetime.Token);
            if (disposed || version != generation) return;
            if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() > 128) throw new IOException("预设列表超出界面边界。");
            projecting = true;
            try
            {
                Presets.Clear(); Presets.Add(new("当前设置", null));
                foreach (var value in result.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("bands", out var bands)
                        || bands.ValueKind != JsonValueKind.Array || bands.GetArrayLength() != Bands.Count) continue;
                    var state = JsonNode.Parse(value.GetRawText())!.AsObject();
                    var name = state["name"]?.ToString() ?? state["id"]?.ToString() ?? "预设";
                    if (name.Length > 160) continue;
                    Presets.Add(new(name, state));
                }
                SelectedPreset = Presets[0];
            }
            finally { projecting = false; }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) { if (!disposed && version == generation) Message = "预设读取失败；仍可手动调整或刷新重试。"; }
    }

    partial void OnSelectedPresetChanged(EqPresetChoice? value)
    {
        if (projecting || !CanEdit || !IsEq || confirmed is null || value is null) return;
        if (value.State is not { } preset) { Project(confirmed); return; }
        var draft = (JsonObject)confirmed.DeepClone();
        draft["preampDb"] = preset["preampDb"]?.DeepClone() ?? JsonValue.Create(0);
        draft["strengthPercent"] = preset["strengthPercent"]?.DeepClone() ?? JsonValue.Create(100);
        draft["tiltDb"] = preset["tiltDb"]?.DeepClone() ?? JsonValue.Create(0);
        draft["bands"] = preset["bands"]!.DeepClone();
        Project(draft, updateConfirmed: false); IsDirty = true;
    }
}
