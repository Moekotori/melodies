using Avalonia.Platform.Storage;
using Echo.Contracts.Library;
using Echo.Contracts.Settings;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Backup;
using Echo.Infrastructure.Preferences;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class BackupSettingsModule : IAsyncDisposable
{
    private readonly LocalBackupService _backup;
    private readonly AutomaticBackupService _automatic;
    private readonly ShellViewModel _shell;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public BackupSettingsModule(SettingsFeatureRuntime runtime, SettingsDialogs dialogs, MainWindow window, ShellViewModel shell,
        string directory, Func<DesktopPreferences> capture, Action<DesktopPreferences> apply)
    {
        _backup = new(directory, shell.AlbumLibrary as ILibraryMaintenance);
        _shell = shell; _automatic = new(_backup); _automatic.Status += BackupStatus;
        shell.FeaturePreferences.Changed += ScheduleChanged;
        foreach (var id in new[] { "automatic-backup-enabled", "automatic-backup-hours", "automatic-backup-retention" })
            runtime.RegisterChange(id, (value, token) => ConfigureScheduleAsync(id, value, token));
        runtime.RegisterAction("settings-export", async (action, token) =>
        {
            if (action == "导出设置")
            {
                var output = await window.StorageProvider.SaveFilePickerAsync(new() { Title = "导出 ECHO 设置", SuggestedFileName = "echo-settings.json", DefaultExtension = "json" });
                if (output?.TryGetLocalPath() is not { } path) throw new OperationCanceledException("已取消。");
                await SettingsTransfer.WriteAsync(path, capture(), token);
                shell.Settings.Feedback = "设置已导出。账号密钥不包含在此文件中。";
            }
            else
            {
                var input = await window.StorageProvider.OpenFilePickerAsync(new() { Title = "导入 ECHO 设置", AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("ECHO 设置") { Patterns = ["*.json"] }] });
                if (input.FirstOrDefault()?.TryGetLocalPath() is not { } path) throw new OperationCanceledException("已取消。");
                var imported = await SettingsTransfer.ReadAsync(path, token);
                await dialogs.ConfirmAsync("导入设置", "将替换当前界面、语言、主题和功能偏好。导入前会备份当前设置；音乐和曲库不会被删除。");
                await SettingsTransfer.WriteAsync(Path.Combine(directory, "preferences-before-import.json"), capture(), token);
                apply(imported); shell.Settings.Feedback = "设置已导入；之前的设置已保留为恢复副本。";
            }
        });
        foreach (var id in new[] { "data-backup", "data-package" })
            runtime.RegisterAction(id, async (action, token) =>
            {
                if (action == "选择目录")
                {
                    var folders = await window.StorageProvider.OpenFolderPickerAsync(new() { Title = "选择本地备份目录", AllowMultiple = false });
                    if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) throw new OperationCanceledException("已取消。");
                    shell.FeaturePreferences.Confirm("backup-directory", new(Selection: folder)); return;
                }
                if (action == "打开目录")
                {
                    var folder = shell.FeaturePreferences.Read("backup-directory", new()).Selection;
                    if (folder.Length == 0) throw new IOException("请先选择备份目录。");
                    await window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder)); return;
                }
                if (action is "导入备份" or "恢复入口")
                {
                    var input = await window.StorageProvider.OpenFilePickerAsync(new() { Title = "验证 ECHO 备份", AllowMultiple = false,
                        FileTypeFilter = [new FilePickerFileType("ECHO 备份") { Patterns = ["*.zip"] }] });
                    if (input.FirstOrDefault()?.TryGetLocalPath() is not { } path) throw new OperationCanceledException("已取消。");
                    var manifest = await _backup.ValidateAsync(path, token);
                    await dialogs.ConfirmAsync("恢复 ECHO 备份", $"已验证 {manifest.Files.Length} 个文件。确认后将关闭 ECHO、恢复应用数据并重新启动。当前数据会保留为回滚副本，音乐文件不会被修改。");
                    var request = await BackupRestore.StageAsync(path, directory, token);
                    var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("无法定位 ECHO 可执行文件。"))
                    { UseShellExecute = false, CreateNoWindow = true };
                    start.ArgumentList.Add("--complete-restore"); start.ArgumentList.Add(request);
                    _ = System.Diagnostics.Process.Start(start) ?? throw new IOException("恢复进程未能启动。");
                    window.RequestExit();
                    return;
                }
                var configured = shell.FeaturePreferences.Read("backup-directory", new()).Selection;
                string destination;
                if (action == "立即备份" && configured.Length > 0)
                    destination = Path.Combine(configured, "echo-backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + ".zip");
                else
                {
                    var output = await window.StorageProvider.SaveFilePickerAsync(new() { Title = "创建 ECHO 数据备份", SuggestedFileName = "echo-backup.zip", DefaultExtension = "zip" });
                    destination = output?.TryGetLocalPath() ?? throw new OperationCanceledException("已取消。");
                }
                await _backup.CreateAsync(destination, token); shell.Settings.Feedback = "一致性备份已完成。";
            });
    }
    private Task ConfigureScheduleAsync(string id, FeatureSetting? desired, CancellationToken token)
    {
        FeatureSetting Read(string key, FeatureSetting fallback) => id == key && desired is not null ? desired : _shell.FeaturePreferences.Read(key, fallback);
        var enabled = Read("automatic-backup-enabled", new()).Enabled;
        if (!double.TryParse(Read("automatic-backup-hours", new(false, "24")).Selection,
            System.Globalization.CultureInfo.InvariantCulture, out var hours)) throw new ArgumentException("无效备份间隔。");
        if (!int.TryParse(Read("automatic-backup-retention", new(false, "5")).Selection, out var retention)) throw new ArgumentException("无效保留份数。");
        return _automatic.ConfigureAsync(enabled, _shell.FeaturePreferences.Read("backup-directory", new()).Selection, hours, retention, token);
    }
    private void ScheduleChanged(string id)
    { if (!_disposed && (id.Length == 0 || id == "backup-directory")) _ = RestoreScheduleAsync(); }
    private async Task RestoreScheduleAsync()
    {
        try { await ConfigureScheduleAsync("", null, _lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposed) _shell.Settings.Feedback = error.Message; }
    }
    private void BackupStatus(string message) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    { if (!_disposed) _shell.Settings.Feedback = message; });
    public async ValueTask DisposeAsync()
    {
        _disposed = true; _lifetime.Cancel(); _shell.FeaturePreferences.Changed -= ScheduleChanged;
        _automatic.Status -= BackupStatus; await _automatic.DisposeAsync(); _lifetime.Dispose();
    }
}
