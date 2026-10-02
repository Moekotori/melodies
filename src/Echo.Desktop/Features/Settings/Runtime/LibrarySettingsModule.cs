using Avalonia.Platform.Storage;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class LibrarySettingsModule
{
    public LibrarySettingsModule(SettingsFeatureRuntime runtime, SettingsDialogs dialogs, MainWindow window, ShellViewModel shell)
    {
        foreach (var id in new[] { "hidden-tracks", "auto-hide-duplicates", "danger-duplicates" })
            runtime.RegisterAction(id, (_, token) => LibraryReviewDialog.ShowAsync(window,
                shell.AlbumLibrary as ILibraryReview ?? throw new NotSupportedException("当前曲库未提供曲目检查端口。"),
                id == "hidden-tracks", dialogs, shell.NotifyHomeDataChanged, token));
        foreach (var id in new[] { "library-health", "danger-database" })
            runtime.RegisterAction(id, async (action, token) =>
            {
                var maintenance = shell.AlbumLibrary as ILibraryMaintenance ?? throw new IOException("当前曲库未提供维护端口。");
                if (action == "创建快照")
                {
                    var target = await window.StorageProvider.SaveFilePickerAsync(new() { Title = "创建曲库健康快照", SuggestedFileName = "echo-library.sqlite", DefaultExtension = "sqlite" });
                    if (target?.TryGetLocalPath() is not { } path) throw new OperationCanceledException("已取消。");
                    await maintenance.BackupAsync(path, token);
                    await dialogs.ShowAsync("快照已创建", "曲库快照已通过现有数据库 owner 一致性备份。原曲库和音乐文件没有被修改。");
                }
                else
                {
                    var health = await maintenance.CheckAsync(token);
                    await dialogs.ShowAsync("曲库检查", $"完整性：{health.Integrity}\n曲目：{health.Tracks:N0}\n路径不可用：{health.Missing:N0}\n\n检查不会修改数据库或音乐文件。");
                }
            });
        runtime.RegisterAction("library-folders", async (_, token) =>
        {
            var folders = await window.StorageProvider.OpenFolderPickerAsync(new() { Title = "添加音乐文件夹", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) throw new OperationCanceledException("已取消。");
            if (shell.ImportLibrary is null) throw new IOException("当前曲库没有导入端口。");
            var report = await shell.ImportLibrary(path, new Progress<Echo.Contracts.Scanning.ScanProgress>(progress =>
                shell.Settings.Feedback = $"{progress.Phase} · 已处理 {progress.Processed:N0} 项"), token);
            shell.NotifyHomeDataChanged();
            await dialogs.ShowAsync("导入结果", $"{report.Status}\n新增：{report.Inserted:N0}\n更新：{report.Updated:N0}\n跳过：{report.Unchanged:N0}\n失败：{report.Failed:N0}");
        });
    }
}
