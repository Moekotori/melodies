using Echo.Core.Contracts;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings.Runtime;

// Controls submit one Core-owned maintenance task. Closing this adapter only stops its UI wait.
public sealed class LibraryAutomationModule : IDisposable
{
    private readonly ShellViewModel shell;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ICoreTaskControl? core;
    private bool disposed;
    public LibraryAutomationModule(SettingsFeatureRuntime runtime, ShellViewModel shell)
    {
        this.shell = shell; core = shell.AlbumLibrary as ICoreTaskControl;
        if (core is null) return;
        runtime.RegisterChange("live-library-updates", async (value, token) =>
        {
            var count = await core.SetAllFolderWatchAsync(value.Enabled, token);
            shell.Settings.Feedback = $"已{(value.Enabled ? "开启" : "关闭")} {count} 个根目录的自动发现。";
        });
        runtime.RegisterAction("reread-metadata", async (action, token) =>
        {
            var progress = new Progress<CoreTaskSnapshot>(state =>
            {
                if (disposed) return;
                shell.Settings.Feedback = state.ResourceProgress is { } resources ? $"资源检查 · {resources.Checked:N0}/{resources.Total:N0}"
                    : state.ScanProgress is { } scan ? $"重读资料 · {scan.Processed:N0} · 失败 {scan.Failed:N0}" : "Core 正在维护曲库。";
            });
            await core.RunMaintenanceAsync(action, progress, token);
            if (!disposed) { shell.NotifyHomeDataChanged(); shell.Settings.Feedback = "曲库维护已完成。"; }
        });
        runtime.RegisterAction("library-maintenance-cancel", (_, token) => core.CancelActiveTaskAsync(token));
        shell.FeaturePreferences.Changed += Restored;
    }
    private void Restored(string id)
    { if (!disposed && id.Length == 0 && shell.FeaturePreferences.Values.ContainsKey("live-library-updates")) _ = RestoreWatchAsync(); }
    private async Task RestoreWatchAsync()
    {
        try { await core!.SetAllFolderWatchAsync(shell.FeaturePreferences.Read("live-library-updates", new()).Enabled, lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) shell.Settings.Feedback = error.Message; }
    }
    public void Dispose() { disposed = true; shell.FeaturePreferences.Changed -= Restored; lifetime.Cancel(); }
}
