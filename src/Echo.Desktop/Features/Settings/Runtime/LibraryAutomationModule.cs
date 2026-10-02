using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class LibraryAutomationModule : IDisposable
{
    private readonly ShellViewModel shell;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? maintenance;
    private bool disposed;
    public LibraryAutomationModule(SettingsFeatureRuntime runtime, ShellViewModel shell)
    {
        this.shell = shell;
        if (shell.AlbumLibrary is not IFolderLibrary || shell.AlbumLibrary is not IFolderOperations) return;
        runtime.RegisterChange("live-library-updates", (value, token) => SetWatchAsync(value.Enabled, token));
        runtime.RegisterAction("reread-metadata", RunMaintenanceAsync);
        runtime.RegisterAction("library-maintenance-cancel", (_, _) => { maintenance?.Cancel(); return Task.CompletedTask; });
        shell.FeaturePreferences.Changed += Restored;
    }
    private async Task<IReadOnlyList<FolderRow>> RootsAsync(int maximum, CancellationToken token)
    {
        var page = await ((IFolderLibrary)shell.AlbumLibrary!).GetFoldersAsync(new(Limit: maximum), token);
        if (page.HasMore) throw new InvalidOperationException($"此操作最多处理 {maximum} 个根目录；请使用文件夹页分别操作更多目录。");
        return page.Items;
    }
    private async Task SetWatchAsync(bool enabled, CancellationToken token)
    {
        var roots = await RootsAsync(16, token); var changed = 0;
        try
        {
            foreach (var root in roots) { await ((IFolderOperations)shell.AlbumLibrary!).SetFolderWatchAsync(root.Path, enabled, token); changed++; }
            shell.Settings.Feedback = $"已{(enabled ? "开启" : "关闭")} {changed} 个已有根目录的自动发现。";
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { throw new IOException($"已有 {changed} 个目录完成更新；其余目录未完成，请在文件夹页检查。{error.Message}", error); }
    }
    private async Task RunMaintenanceAsync(string action, CancellationToken cancellation)
    {
        if (maintenance is not null) throw new IOException("已有曲库维护正在执行，请先取消或等待完成。");
        using var current = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        maintenance = current;
        try
        {
            var roots = await RootsAsync(64, current.Token); var operations = (IFolderOperations)shell.AlbumLibrary!;
            for (var index = 0; index < roots.Count; index++)
            {
                current.Token.ThrowIfCancellationRequested(); var root = roots[index];
                if (action == "检查封面与歌词")
                    await operations.CheckFolderResourcesAsync(new(root.Path), new Progress<FolderResourceProgress>(progress =>
                    { if (!disposed) shell.Settings.Feedback = $"资源检查 · {progress.Checked:N0}/{progress.Total:N0}"; }), current.Token);
                else if (action == "重新读取全部歌曲")
                {
                    var report = await operations.ScanFolderAsync(new(root.Path, FolderScanMode.EmbeddedTags), new Progress<ScanProgress>(progress =>
                    { if (!disposed) shell.Settings.Feedback = $"重读资料 · {progress.Processed:N0} · 失败 {progress.Failed:N0}"; }), current.Token);
                    if (report.Status == ScanStatus.Cancelled) throw new OperationCanceledException(current.Token);
                    if (report.Status != ScanStatus.Completed) throw new IOException("资料重读未完成，请查看曲库扫描记录。");
                }
                else throw new ArgumentException("不支持的维护动作。");
            }
            shell.NotifyHomeDataChanged(); if (!disposed) shell.Settings.Feedback = $"维护已完成，共处理 {roots.Count} 个根目录。";
        }
        finally { maintenance = null; }
    }
    private void Restored(string id)
    {
        if (!disposed && id.Length == 0 && shell.FeaturePreferences.Snapshot().ContainsKey("live-library-updates")) _ = RestoreWatchAsync();
    }
    private async Task RestoreWatchAsync()
    {
        try { await SetWatchAsync(shell.FeaturePreferences.Read("live-library-updates", new()).Enabled, lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) shell.Settings.Feedback = error.Message; }
    }
    public void Dispose()
    { disposed = true; shell.FeaturePreferences.Changed -= Restored; lifetime.Cancel(); maintenance?.Cancel(); }
}
