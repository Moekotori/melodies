using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    private JsonObject? activeHeadphoneProfile;
    private bool headphoneGainApproved;
    [ObservableProperty] private bool headphoneGainWarning;
    private double pendingHeadphoneGain;
    public JsonObject? HeadphonePreviewPreset => headphonePreview?[CorrectionMode.Key] as JsonObject;
    public JsonObject? HeadphoneOriginalPreset => headphonePreview?["original"] as JsonObject;
    public bool HasHeadphonePreview => HeadphonePreviewPreset is not null;
    public bool HeadphoneShowsOriginal => HasHeadphonePreview && CorrectionMode.Key == "listening";
    public string HeadphonePreviewFacts => HeadphonePreviewPreset is { } preset
        ? $"{preset["bands"]!.AsArray().Count(x => x?["enabled"]?.ToString() != "false")} 个滤波器 · 前级 {Number(preset, "preampDb"):0.###} dB" : "选择型号和校正来源以预览曲线";
    public double HeadphoneDefaultPreamp => activeHeadphoneProfile?[activeHeadphoneProfile["mode"]?.ToString() ?? "original"] is JsonObject preset ? Number(preset, "preampDb") : 0;
    public decimal HeadphonePreampValue { get => (decimal)PreampDb; set => RequestHeadphonePreamp((double)value); }
    public double HeadphonePreampDb { get => PreampDb; set => RequestHeadphonePreamp(value); }
    public bool CanResetHeadphonePreamp => CanEdit && activeHeadphoneProfile is not null;
    public string HeadphonePreampHint => $"来源默认 {HeadphoneDefaultPreamp:0.###} dB · 手动调整可还原";
    public string ActiveHeadphoneLabel => activeHeadphoneProfile is { } profile
        ? $"当前校正：{profile["vendor"]} {profile["product"]} · {profile["author"]} · {(profile["mode"]?.ToString() == "listening" ? "听感调整" : "作者原始曲线")}" : "当前为手动校正参数";

    private void NotifyHeadphonePreview()
    {
        foreach (var name in new[] { nameof(CanLoadHeadphone), nameof(HeadphonePreviewPreset), nameof(HeadphoneOriginalPreset), nameof(HasHeadphonePreview), nameof(HeadphoneShowsOriginal), nameof(HeadphonePreviewFacts) }) OnPropertyChanged(name);
    }
    private void ProjectHeadphoneProfile(JsonObject state)
    {
        activeHeadphoneProfile = state["correctionProfile"]?.DeepClone() as JsonObject;
        if (headphonePreview is null && activeHeadphoneProfile is { } profile)
        {
            headphonePreview = (JsonObject)profile.DeepClone();
            CorrectionMode = CorrectionModes.First(x => x.Key == profile["mode"]?.ToString());
            headphoneProduct = new(profile["productId"]?.ToString() ?? "", profile["product"]?.ToString() ?? "", profile["vendor"]?.ToString() ?? "", 0);
            HeadphoneAttribution = $"作者：{profile["author"]} · OPRA · CC-BY-SA-4.0\n{profile["details"]}\n{profile["link"]}";
            NotifyHeadphonePreview();
        }
        RevokeHeadphoneGainConsent();
        foreach (var name in new[] { nameof(HeadphonePreampValue), nameof(HeadphonePreampHint), nameof(HeadphoneDefaultPreamp), nameof(CanResetHeadphonePreamp), nameof(ActiveHeadphoneLabel) }) OnPropertyChanged(name);
        NotifyHeadphoneBrowser();
    }

    [RelayCommand] private void LoadHeadphone()
    {
        if (!CanLoadHeadphone || !IsHeadphone || HeadphonePreviewPreset is not { } preset || headphonePreview is null) return;
        var profile = (JsonObject)headphonePreview.DeepClone(); profile["mode"] = CorrectionMode.Key;
        var draft = (JsonObject)preset.DeepClone(); draft["correctionProfile"] = profile;
        UsePresetDraft(draft); Enabled = true;
        Message = "正在提交独立耳机校正试听；应用后保存。开启校正会退出 Bit-perfect。";
    }
    partial void OnCorrectionModeChanged(DspChoice value)
    {
        RevokeHeadphoneGainConsent(); NotifyHeadphonePreview();
        if (!projecting && activeHeadphoneProfile?["id"]?.ToString() == headphonePreview?["id"]?.ToString() && activeHeadphoneProfile is not null) LoadHeadphone();
    }

    public void RequestHeadphonePreamp(double value)
    {
        if (!CanEdit || !IsHeadphone || !double.IsFinite(value) || value is < -60 or > 24) return;
        if (Math.Abs(value - PreampDb) <= 1e-9) return;
        if (value > PreampDb && value > HeadphoneDefaultPreamp && !headphoneGainApproved)
        { pendingHeadphoneGain = value; HeadphoneGainWarning = true; OnPropertyChanged(nameof(HeadphonePreampValue)); OnPropertyChanged(nameof(HeadphonePreampDb)); return; }
        PreampDb = value; OnPropertyChanged(nameof(HeadphonePreampValue));
    }
    [RelayCommand] private void AcceptHeadphoneGain()
    {
        if (!HeadphoneGainWarning || !CanEdit || !IsHeadphone) return;
        headphoneGainApproved = true; HeadphoneGainWarning = false;
        PreampDb = pendingHeadphoneGain; OnPropertyChanged(nameof(HeadphonePreampValue));
    }
    [RelayCommand] private void CancelHeadphoneGain() { HeadphoneGainWarning = false; OnPropertyChanged(nameof(HeadphonePreampValue)); OnPropertyChanged(nameof(HeadphonePreampDb)); }
    [RelayCommand] private void ResetHeadphonePreamp()
    {
        if (!CanResetHeadphonePreamp) return;
        RevokeHeadphoneGainConsent(); PreampDb = HeadphoneDefaultPreamp; OnPropertyChanged(nameof(HeadphonePreampValue));
    }
    private void RevokeHeadphoneGainConsent() { headphoneGainApproved = false; HeadphoneGainWarning = false; }

    [RelayCommand] private void ClearHeadphone()
    {
        if (!CanEdit || !IsHeadphone || confirmed is null) return;
        var draft = (JsonObject)confirmed.DeepClone(); draft["correctionProfile"] = null; draft["enabled"] = false;
        draft["presetName"] = "未选择耳机校正"; Project(draft, updateConfirmed: false); IsDirty = true; NoteEdit();
        Message = "校正已请求旁路；应用后清除选择。";
    }
}
