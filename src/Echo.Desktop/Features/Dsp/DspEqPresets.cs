using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Core.Contracts;

namespace Echo.Desktop.Features.Dsp;

public sealed record EqPresetChoice(string Label, JsonObject? State, string? Id = null, bool User = false);

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
            var library = await ReadLibraryAsync();
            if (disposed || version != generation) return;
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
                AppendUserPresets(library);
                SelectedPreset = Presets[0];
            }
            finally { projecting = false; }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) { if (!disposed && version == generation) Message = "预设读取失败；仍可手动调整或刷新重试。"; }
    }

    partial void OnSelectedPresetChanged(EqPresetChoice? value)
    {
        if (projecting || !CanEdit || !SelectedModule.EditsEqualizer || confirmed is null || value is null) return;
        if (value.State is not { } preset) { Project(confirmed); HearConfirmed(); return; }
        if (IsHeadphone && HasPersonalProcessing(preset)) { Message = "此预设包含声道或动态 EQ，请在个人 EQ 中使用。"; return; }
        UsePresetDraft(preset);
        OnPropertyChanged(nameof(CanDeletePreset));
    }

    public bool CanDeletePreset => CanUseResources && SelectedPreset is { User: true };

    private void AppendUserPresets(JsonObject library)
    {
        if (library["presets"] is not JsonArray presets) return;
        foreach (var node in presets.Take(32))
        {
            if (node is JsonObject authored && !IsHeadphone && authored["headphoneCorrection"]?.ToString() == "true") continue;
            if (node is not JsonObject preset || preset["bands"] is not JsonArray bands || bands.Count != Bands.Count) continue;
            if (IsHeadphone && HasPersonalProcessing(preset)) continue;
            var name = preset["name"]?.ToString()?.Trim();
            var id = preset["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || name.Length > 80) continue;
            Presets.Add(new(name, preset, id, true));
        }
    }

    [RelayCommand] private async Task SavePresetAsync()
    {
        if (!CanUseResources || !SelectedModule.EditsEqualizer || Bands.Count == 0) return;
        ResourceBusy = true;
        try
        {
            var name = string.IsNullOrWhiteSpace(PresetName) ? "我的预设" : PresetName.Trim();
            var sameScope = (SelectedPreset?.State?["headphoneCorrection"]?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) == true) == IsHeadphone;
            var id = sameScope && SelectedPreset is { User: true, Id: { } current } ? current : "user-" + Guid.NewGuid().ToString("N");
            var draft = CaptureDraft(); draft["headphoneCorrection"] = IsHeadphone;
            await resources.ExecuteAsync(DspResourceOperation.SavePreset, id: id, name: name, state: Json(draft), token: lifetime.Token);
            var library = await ReadLibraryAsync();
            projecting = true;
            try
            {
                foreach (var old in Presets.Where(x => x.User).ToArray()) Presets.Remove(old);
                AppendUserPresets(library); SelectedPreset = Presets.FirstOrDefault(x => x.Id == id);
            }
            finally { projecting = false; }
            Message = "预设已保存。"; OnPropertyChanged(nameof(CanDeletePreset));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed) Message = "保存失败：" + error.Message; }
        finally { if (!disposed) ResourceBusy = false; }
    }
    [RelayCommand] private async Task DeletePresetAsync()
    {
        if (!CanUseResources || SelectedPreset is not { User: true, Id: { } id } selected) return;
        ResourceBusy = true;
        try
        {
            await resources.ExecuteAsync(DspResourceOperation.DeletePreset, id: id, token: lifetime.Token);
            Presets.Remove(selected); projecting = true;
            try { SelectedPreset = Presets.FirstOrDefault(); } finally { projecting = false; }
            OnPropertyChanged(nameof(CanDeletePreset)); Message = "预设已删除。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed) Message = "删除失败：" + error.Message; }
        finally { if (!disposed) ResourceBusy = false; }
    }
}
