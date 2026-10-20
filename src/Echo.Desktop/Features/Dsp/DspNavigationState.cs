using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    // Explicit page refresh only: small configuration replies, no meter subscriptions or polling.
    [RelayCommand]
    private async Task RefreshPageAsync()
    {
        await RefreshAsync();
        if (disposed || !IsReady || session is null) return;
        var version = generation;
        IsBusy = true;
        try
        {
            foreach (var module in Modules)
            {
                if (module == SelectedModule || !module.ShowsDetail) continue;
                if (module.ConfiguresOutput)
                {
                    var state = Child(session.Snapshot.Processing, module.Id == "src" ? "echoSrc" : "sdm");
                    module.Status = state is null ? "运行状态未确认" : Flag(state, "active") ? "已启用" : "旁路";
                    continue;
                }
                if (module.ReadMethod is null) continue;
                var reply = await session.DspAsync(module.ReadMethod, Json(new JsonArray()), token: lifetime.Token);
                if (disposed || version != generation) return;
                if (reply.ValueKind != JsonValueKind.Object) throw new IOException($"{module.Title}没有返回有效状态。");
                module.Status = NavigationStatus(module.Id, reply);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed && version == generation) Message = "模块状态读取失败：" + error.Message; }
        finally { if (!disposed && version == generation) IsBusy = false; }
    }

    private static string NavigationStatus(string id, JsonElement state)
    {
        if (id == "headroom") return $"{NumberOf(state, "headroomDb"):+0.0;-0.0;0.0} dB";
        if (id == "replayGain") return NumberOf(state, "mode") switch { 1 => "歌曲增益", 2 => "专辑增益", _ => "旁路" };
        return Flag(state, id is "safety" or "limiter" ? "safetyLimiterEnabled" : "enabled") ? "已启用" : "旁路";
    }
}
