using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    private int auditionEpoch;
    private bool auditionWorker;
    public bool CanResetBand => CanEdit && IsEq && SelectedBand is not null;

    partial void OnSelectedModuleChanging(DspModule? oldValue, DspModule newValue)
    {
        if (oldValue?.Id == "headphone" && newValue != oldValue) { headphoneBrowseEpoch++; ResourceBusy = false; RevokeHeadphoneGainConsent(); }
        if (oldValue?.Id == "headroom" && newValue != oldValue) _ = SetHeadroomListenAsync(false);
        if (oldValue?.Id == "deesser" && newValue != oldValue) _ = SetDeesserListenAsync(false);
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
        ++auditionEpoch;
        if (IsEq) EqAuditionStatus = "正在提交试听…";
        if (!auditionWorker) _ = HearLatestAsync();
    }

    // One coalescing worker per page. Fast pointer input never creates a timer per event.
    private async Task HearLatestAsync()
    {
        auditionWorker = true;
        try
        {
            while (!disposed)
            {
                await Task.Delay(90, lifetime.Token);
                var epoch = auditionEpoch;
                await HearAsync(epoch);
                if (epoch == auditionEpoch) break;
            }
        }
        catch (OperationCanceledException) { return; }
        finally { auditionWorker = false; }
    }

    private async Task HearAsync(int epoch)
    {
        if (epoch != auditionEpoch || disposed || !IsDirty || !CanEdit || confirmed is null || session is null) return;
        var module = SelectedModule;
        var state = CaptureDraft();
        try
        {
            await PushAsync(module, state, retain: false, lifetime.Token);
            if (epoch != auditionEpoch || disposed) return;
            if (IsEq) EqAuditionStatus = "宿主已确认试听 · 应用后记入撤销";
            ClearAuditionError();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { if (epoch == auditionEpoch && !disposed) { if (IsEq) EqAuditionStatus = "试听未成功"; Message = "试听失败：" + e.Message; } }
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
        if (module.Id == "rack") await ResolveRackBanksAsync(state);
        if (module.Id == "limiter") return await PushLimiterAsync(state, retain, token);
        var result = await session.DspAsync(module.WriteMethod, Json(ParametersFor(module, state)), retain, token);
        if (retain && module.Id == "headphone" && Bool(state, "enabled") && state["correctionProfile"]?["productId"]?.ToString() is { Length: > 0 } productId)
        {
            try { await resources.ExecuteAsync(Echo.Core.Contracts.DspResourceOperation.RecentOpra, id: productId, token: token); }
            catch (Exception e) when (e is not OperationCanceledException) { if (!disposed) Message = "校正已应用，最近使用记录未保存：" + e.Message; }
        }
        return result;
    }

    private async Task<JsonElement> PushRoomAsync(JsonObject state, bool retain, CancellationToken token)
    {
        if (session is null) throw new IOException("播放服务尚未连接。");
        var path = state["irPath"]?.ToString() ?? "";
        var bankId = state["bankId"]?.ToString() ?? state["irId"]?.ToString();
        if (Guid.TryParseExact(bankId, "N", out _) && !Bool(state, "clearIr"))
        {
            var bank = await resources.ExecuteAsync(Echo.Core.Contracts.DspResourceOperation.ResolveBank, id: bankId, token: token);
            path = bank.GetProperty("nativePath").GetString()!;
        }
        if (state["clearIr"] is JsonValue cleared && cleared.TryGetValue<bool>(out var shouldClear) && shouldClear)
        { await session.DspAsync("roomCorrection.clear", Json(new JsonArray()), retain, token); loadedIrPath = ""; }
        else if (path.Length > 0 && (retain || path != loadedIrPath))
        {
            var before = await session.DspAsync("roomCorrection.getState", Json(new JsonArray()), token: token);
            var awaitingFormat = session.Snapshot.Current is null && before.TryGetProperty("channelMode", out var mode) && mode.GetString() == "none"
                && before.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
            if (awaitingFormat) await session.DspAsync("roomCorrection.setEnabled", Json(new JsonArray(false)), token: token);
            try
            {
                var file = Path.GetFileName(path);
                var loaded = await session.DspAsync("roomCorrection.loadIr", Json(new JsonArray(new JsonObject { ["path"] = path, ["irId"] = bankId ?? file, ["irName"] = state["irName"]?.ToString() is { Length: > 0 } name ? name : file, ["forceReload"] = true })), retain, token);
                if (loaded.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(error.GetString())) throw new IOException(error.GetString());
                loadedIrPath = path;
            }
            catch
            {
                if (awaitingFormat) await session.DspAsync("roomCorrection.setEnabled", Json(new JsonArray(true)), token: CancellationToken.None);
                throw;
            }
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
