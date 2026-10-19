using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Echo.Desktop.Shell;
using Avalonia.Platform.Storage;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class DiagnosticsModule
{
    private readonly string _directory;
    public DiagnosticsModule(SettingsFeatureRuntime runtime, SettingsDialogs dialogs, MainWindow window, ShellViewModel shell, string directory)
    {
        _directory = directory;
        foreach (var id in new[] { "library-performance-diagnostics", "library-lab", "dev-console", "safe-mode", "diagnostics-assistant", "diagnostics" })
            runtime.RegisterAction(id, async (action, token) =>
            {
                var report = await BuildAsync(shell, token);
                if (action.Contains("导出", StringComparison.Ordinal))
                {
                    var target = await window.StorageProvider.SaveFilePickerAsync(new() { Title = "导出诊断", SuggestedFileName = "melodies-diagnostics.md", DefaultExtension = "md" });
                    if (target?.TryGetLocalPath() is { } path) await File.WriteAllTextAsync(path, report, token);
                }
                else if (action.Contains("打开报告文件夹", StringComparison.Ordinal))
                { await Task.Run(() => Directory.CreateDirectory(Path.Combine(directory, "reports")), token); await window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.Combine(directory, "reports"))); }
                else await dialogs.ShowAsync("melodies 本机诊断", report);
            });
        runtime.RegisterAction("about-contributors", (_, _) => dialogs.ShowAsync("贡献者", "melodies\n\nMoekotori 与 melodies 社区贡献者\n\n源代码与完整贡献记录：\nhttps://github.com/Moekotori/ECHO/graphs/contributors\n\n本机运行不会查询或上传账号信息。"));
        runtime.RegisterAction("about-special-thanks", (_, _) => dialogs.ShowAsync("特别鸣谢", "感谢 melodies 用户、测试者、翻译者与开源项目贡献者。\n\nAvalonia · .NET · SQLite · FFmpeg\n\n第三方组件许可保留在随应用分发的许可证中。"));
    }
    private static Task<string> BuildAsync(ShellViewModel shell, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); using var process = Process.GetCurrentProcess();
        var result = new StringBuilder("# melodies 本机诊断\n\n");
        result.AppendLine($"OS: {RuntimeInformation.OSDescription}\nRuntime: {RuntimeInformation.FrameworkDescription}\nManaged heap: {GC.GetTotalMemory(false):N0} bytes\nProcess private bytes: {process.PrivateMemorySize64:N0}\nWorking set: {process.WorkingSet64:N0}");
        var state = shell.Player.Session.Snapshot;
        result.AppendLine($"\nPlayback: {state.Status}\nPosition: {state.PositionSeconds:0.00}\nSample rate: {state.SampleRate}\nChannels: {state.Channels}\nQueue count: {shell.Player.Session.Queue.Count}");
        result.AppendLine("\n该报告不包含凭据、文件路径或歌曲内容；尚未测量的 CPU、设备漏音和长期泄漏指标没有推断值。");
        return result.ToString();
    }, token);
}
