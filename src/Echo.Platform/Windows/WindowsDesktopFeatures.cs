using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Echo.Platform.Windows;

public static class WindowsDesktopFeatures
{
    private const string StartupPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupName = "ECHO Avalonia";
    public static Task SetStartupAsync(bool enabled, string executable, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("此平台未提供登录启动适配。");
        var path = Path.GetFullPath(executable);
        if (!File.Exists(path) || Path.GetExtension(path) != ".exe") throw new IOException("请使用已发布的 ECHO 可执行文件设置登录启动。");
        using var key = Registry.CurrentUser.CreateSubKey(StartupPath, true);
        if (enabled) key.SetValue(StartupName, $"\"{path}\" --autostart", RegistryValueKind.String);
        else key.DeleteValue(StartupName, false);
        if ((key.GetValue(StartupName) is string) != enabled) throw new IOException("登录启动设置未能确认，请重试。");
    }, token);

    public static Task SetTouchKeyboardAsync(bool enabled, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("此平台未提供屏幕键盘适配。");
        if (enabled) Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "osk.exe")) { UseShellExecute = true });
    }, token);

    public static void KeepAwake(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = SetThreadExecutionState(enabled ? 0x80000001u : 0x80000000u);
        if (result == 0) throw new IOException("系统未接受播放期间保持唤醒的请求。");
    }
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
}
