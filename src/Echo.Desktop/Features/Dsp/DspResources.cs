using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Core.Client;
using Echo.Core.Contracts;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    private readonly IDspResourceClient resources;
    private JsonNode? presetAttribution;
    [ObservableProperty] private bool resourceBusy;
    public bool CanUseResources => CanEdit && !ResourceBusy;
    partial void OnResourceBusyChanged(bool value) { OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(CanUseResources)); OnPropertyChanged(nameof(CanLoadHeadphone)); OnPropertyChanged(nameof(HasPreviousHeadphones)); }
    private async Task<JsonObject> ReadLibraryAsync()
    {
        var result = await resources.ExecuteAsync(DspResourceOperation.Library, token: lifetime.Token);
        return JsonNode.Parse(result.GetRawText())!.AsObject();
    }
    public async Task ImportPresetAsync(string path)
    {
        if (!CanUseResources || !SelectedModule.EditsEqualizer || confirmed is null) return;
        var version = generation; ResourceBusy = true;
        try
        {
            var result = await resources.ExecuteAsync(DspResourceOperation.ImportEqualizer, path: path, token: lifetime.Token);
            if (disposed || generation != version) return;
            var preset = JsonNode.Parse(result.GetRawText())!.AsObject();
            if (IsEq && (Number(preset, "preampDb") is < -12 or > 6 || preset["bands"]!.AsArray().Any(x => x is JsonObject b && (Number(b, "gainDb") is < -24 or > 12 || Number(b, "q") > (b["filterType"]?.ToString() == "notch" ? 24 : 12) || Number(b, "q") < .1 || Number(b, "frequencyHz") < 20))))
                throw new IOException("作者参数超过个人 EQ 范围，请在耳机校正通道导入；参数未截断。");
            if (IsHeadphone && HasPersonalProcessing(preset)) throw new IOException("此预设包含声道或动态 EQ，请在个人 EQ 中导入；未丢弃这些参数。");
            UsePresetDraft(preset); Message = "参数已导入，可检查并应用。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed && generation == version) Message = "导入失败：" + error.Message; }
        finally { if (!disposed) ResourceBusy = false; }
    }
    private static bool HasPersonalProcessing(JsonObject preset) => preset["bands"] is JsonArray bands && bands.OfType<JsonObject>().Any(x => x["scope"]?.ToString() is { } scope && scope != "stereo" || x["dynamicMode"]?.ToString() is { } mode && mode != "off");
    private void UsePresetDraft(JsonObject preset)
    {
        if (confirmed is null) return;
        var draft = (JsonObject)confirmed.DeepClone();
        foreach (var key in new[] { "bands", "preampDb", "strengthPercent", "tiltDb", "attribution" })
            if (preset[key] is { } value) draft[key] = value.DeepClone();
        draft["presetName"] = preset["name"]?.DeepClone() ?? JsonValue.Create("导入预设");
        draft["presetId"] = preset["id"]?.DeepClone() ?? JsonValue.Create("custom");
        presetAttribution = preset["attribution"]?.DeepClone();
        Project(draft, updateConfirmed: false);
        SelectedBand = Bands.FirstOrDefault(x => x.Enabled && Math.Abs(x.GainDb) > .01) ?? Bands.FirstOrDefault(x => x.Enabled) ?? Bands.FirstOrDefault();
        IsDirty = true; NoteEdit();
    }
    public string ExportCurrentPreset()
    {
        if (!CanEdit || !SelectedModule.EditsEqualizer || confirmed is null) throw new IOException("均衡器尚未就绪。");
        var draft = CaptureDraft(); draft["name"] = PresetName; return draft.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
