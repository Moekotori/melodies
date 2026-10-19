using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    private int auditionEpoch;
    public bool CanResetBand => CanEdit && IsEq && SelectedBand is not null;

    partial void OnSelectedModuleChanging(DspModule? oldValue, DspModule newValue)
    {
        if (oldValue is null || oldValue.DraftOnly || newValue == oldValue || IsBusy || !IsDirty || confirmed is null || session is null || oldValue.WriteMethod is null) return;
        auditionEpoch++;
        var state = CaptureDraft();
        _ = CommitModuleAsync(oldValue, state);
    }

    public string CurveScopeLabel => "参数响应示意 · " + (SelectedBand?.Scope.Label ?? "立体声") + " · 不含动态实时增益";
    partial void OnSelectedBandChanged(EqBandViewModel? value)
    { OnPropertyChanged(nameof(CanResetBand)); OnPropertyChanged(nameof(CurveScopeLabel)); }

    private void NoteEdit()
    {
        if (projecting || !CanEdit || SelectedModule.DraftOnly || SelectedModule.WriteMethod is null || confirmed is null) return;
        var epoch = ++auditionEpoch;
        _ = HearAsync(epoch);
    }

    private async Task HearAsync(int epoch)
    {
        try { await Task.Delay(90, lifetime.Token); }
        catch (OperationCanceledException) { return; }
        if (epoch != auditionEpoch || disposed || !CanEdit || confirmed is null || session is null) return;
        var module = SelectedModule;
        var state = CaptureDraft();
        try
        {
            await PushAsync(module, state, retain: false, lifetime.Token);
            if (epoch != auditionEpoch || disposed) return;
            ClearAuditionError();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { if (epoch == auditionEpoch && !disposed) Message = "试听失败：" + e.Message; }
    }

    private async Task CommitModuleAsync(DspModule module, JsonObject state)
    {
        if (session is null || module.WriteMethod is null) return;
        try { await PushAsync(module, state, retain: true, CancellationToken.None); }
        catch (Exception) { /* The next visit reads whatever the host kept. */ }
    }

    private void HearConfirmed()
    {
        if (confirmed is null || session is null || SelectedModule.ConfiguresOutput || SelectedModule.WriteMethod is null) return;
        var epoch = ++auditionEpoch;
        var state = (JsonObject)confirmed.DeepClone();
        _ = HearConfirmedAsync(state, epoch);
    }

    private async Task HearConfirmedAsync(JsonObject state, int epoch)
    {
        var module = SelectedModule;
        if (session is null || module.WriteMethod is null) return;
        try
        {
            await PushAsync(module, state, retain: false, lifetime.Token);
            if (epoch == auditionEpoch && !disposed) ClearAuditionError();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { if (epoch == auditionEpoch && !disposed) Message = "还原试听失败：" + e.Message; }
    }

    private void ClearAuditionError()
    {
        if (Message.StartsWith("试听失败", StringComparison.Ordinal) || Message.StartsWith("还原试听失败", StringComparison.Ordinal)) Message = "";
    }

    private async Task<JsonElement> PushAsync(DspModule module, JsonObject state, bool retain, CancellationToken token)
    {
        if (session is null || module.WriteMethod is null) throw new IOException("播放服务尚未连接。");
        if (module.Id == "room") return await PushRoomAsync(state, retain, token);
        if (module.Id == "limiter") return await PushLimiterAsync(state, retain, token);
        return await session.DspAsync(module.WriteMethod, Json(ParametersFor(module, state)), retain, token);
    }

    private async Task<JsonElement> PushRoomAsync(JsonObject state, bool retain, CancellationToken token)
    {
        if (session is null) throw new IOException("播放服务尚未连接。");
        var path = state["irPath"]?.ToString() ?? "";
        if (state["clearIr"] is JsonValue cleared && cleared.TryGetValue<bool>(out var shouldClear) && shouldClear)
        { await session.DspAsync("roomCorrection.clear", Json(new JsonArray()), retain, token); loadedIrPath = ""; }
        else if (path.Length > 0 && (retain || path != loadedIrPath))
        {
            var file = Path.GetFileName(path);
            await session.DspAsync("roomCorrection.loadIr", Json(new JsonArray(new JsonObject { ["path"] = path, ["irId"] = file, ["irName"] = file, ["forceReload"] = true })), retain, token);
            loadedIrPath = path;
        }
        await session.DspAsync("roomCorrection.setTrim", Json(new JsonArray(state["trimDb"]?.DeepClone() ?? JsonValue.Create(0))), retain, token);
        return await session.DspAsync("roomCorrection.setEnabled", Json(new JsonArray(state["enabled"]?.DeepClone() ?? JsonValue.Create(false))), retain, token);
    }

    private static JsonArray ParametersFor(DspModule module, JsonObject state) => module.Id switch
    {
        "headroom" => new(state["headroomDb"]!.DeepClone()),
        "safety" => new(state["safetyLimiterEnabled"]?.DeepClone() ?? JsonValue.Create(false)),
        "rack" => new(state),
        _ => new(state),
    };

    [RelayCommand]
    private void ResetBand()
    {
        if (!CanResetBand || SelectedBand is not { } band) return;
        band.GainDb = 0;
    }
}
