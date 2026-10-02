using Echo.Contracts.Settings;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class BroadcastSettingsModule : IDisposable
{
    private readonly ILocalBroadcast service;
    private readonly FeaturePreferences preferences;
    private readonly Action<string> feedback;
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    public BroadcastSettingsModule(SettingsFeatureRuntime runtime, FeaturePreferences preferences, ILocalBroadcast service,
        SettingsDialogs dialogs, Func<string, Task> copy, Action<string> feedback)
    {
        this.service = service; this.preferences = preferences; this.feedback = feedback;
        foreach (var id in new[] { "obs-browser-source", "stage-api" })
            runtime.RegisterChange(id, async (value, token) =>
            {
                var other = id == "stage-api" ? "obs-browser-source" : "stage-api";
                await service.SetEnabledAsync(value.Enabled || preferences.Read(other, new()).Enabled, token);
                feedback(service.IsRunning ? $"本机输出已启动：{service.Address}/overlay；状态接口：{service.Address}/state" : "本机输出已关闭。");
            });
        runtime.RegisterAction("broadcast-links", async (action, _) =>
        {
            if (!service.IsRunning) throw new IOException("请先开启浏览器源输出或 Stage API。");
            if (action == "复制浏览器源地址") await copy(service.Address + "/overlay");
            else if (action == "复制状态接口地址") await copy(service.Address + "/state");
            else await dialogs.ShowAsync("本机输出", $"浏览器源：{service.Address}/overlay\n状态 API：{service.Address}/state\n\n仅监听本机，不对局域网开放。关闭两个开关后释放监听器与连接。");
        });
        preferences.Changed += Restore;
    }
    private void Restore(string id) { if (id.Length == 0 && !disposed) _ = RestoreAsync(); }
    private async Task RestoreAsync()
    {
        try { await service.SetEnabledAsync(preferences.Read("obs-browser-source", new()).Enabled || preferences.Read("stage-api", new()).Enabled, lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) feedback(error.Message); }
    }
    public void Dispose() { disposed = true; preferences.Changed -= Restore; lifetime.Cancel(); }
}
