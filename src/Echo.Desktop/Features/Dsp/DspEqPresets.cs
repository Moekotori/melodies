using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
                AppendUserPresets();
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
        if (value.State is not { } preset) { Project(confirmed); HearConfirmed(); return; }
        var draft = (JsonObject)confirmed.DeepClone();
        draft["preampDb"] = preset["preampDb"]?.DeepClone() ?? JsonValue.Create(0);
        draft["strengthPercent"] = preset["strengthPercent"]?.DeepClone() ?? JsonValue.Create(100);
        draft["tiltDb"] = preset["tiltDb"]?.DeepClone() ?? JsonValue.Create(0);
        draft["bands"] = preset["bands"]!.DeepClone();
        Project(draft, updateConfirmed: false); IsDirty = true; NoteEdit();
        OnPropertyChanged(nameof(CanDeletePreset));
    }

    public bool CanDeletePreset => CanEdit && SelectedPreset is { User: true };

    private void AppendUserPresets()
    {
        if (DspLibraryStore.Load()["presets"] is not JsonArray presets) return;
        foreach (var node in presets.Take(32))
        {
            if (node is not JsonObject preset || preset["bands"] is not JsonArray bands || bands.Count != Bands.Count) continue;
            var name = preset["name"]?.ToString()?.Trim();
            var id = preset["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || name.Length > 40) continue;
            Presets.Add(new(name, preset, id, true));
        }
    }

    [RelayCommand] private void SavePreset()
    {
        if (!CanEdit || !IsEq || Bands.Count == 0) return;
        var name = (PresetName.Trim() is { Length: > 0 } typed ? typed : "我的预设");
        if (name.Length > 40) name = name[..40];
        var preset = CaptureDraft();
        preset["name"] = name; preset["presetName"] = name; preset["id"] = "user-" + Guid.NewGuid().ToString("N")[..8];
        var root = DspLibraryStore.Load();
        var presets = root["presets"] as JsonArray ?? new JsonArray();
        var duplicate = presets.OfType<JsonObject>().FirstOrDefault(x => x["name"]?.ToString() == name);
        if (duplicate is not null) presets.Remove(duplicate);
        presets.Insert(0, preset);
        while (presets.Count > 32) presets.RemoveAt(presets.Count - 1);
        root["presets"] = presets; DspLibraryStore.Save(root);
        projecting = true;
        try
        {
            var existing = Presets.FirstOrDefault(x => x.User && x.Label == name);
            if (existing is not null) Presets.Remove(existing);
            Presets.Add(new(name, preset, preset["id"]!.ToString(), true)); SelectedPreset = Presets[^1];
        }
        finally { projecting = false; }
        OnPropertyChanged(nameof(CanDeletePreset));
        Message = "预设已存在本机。选中它会回到这条曲线，应用后才记入撤销。";
    }

    [RelayCommand] private void DeletePreset()
    {
        if (SelectedPreset is not { User: true, Id: { } id }) return;
        var root = DspLibraryStore.Load();
        if (root["presets"] is JsonArray presets)
        {
            var match = presets.OfType<JsonObject>().FirstOrDefault(x => x["id"]?.ToString() == id);
            if (match is not null) presets.Remove(match);
            DspLibraryStore.Save(root);
        }
        Presets.Remove(SelectedPreset);
        projecting = true;
        try { SelectedPreset = Presets.FirstOrDefault(); }
        finally { projecting = false; }
        OnPropertyChanged(nameof(CanDeletePreset));
    }
}
