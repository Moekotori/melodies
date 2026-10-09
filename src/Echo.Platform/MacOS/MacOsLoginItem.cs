using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace Echo.Platform.MacOS;

/// <summary>Per-user LaunchAgent. Does not register a login item for other accounts.</summary>
public static class MacOsLoginItem
{
    private const string Label = "app.echo.avalonia";

    public static Task SetEnabledAsync(bool enabled, string executable, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("此平台未提供登录启动适配。");
        var path = Path.GetFullPath(executable);
        if (!File.Exists(path)) throw new IOException("无法定位 ECHO 可执行文件。");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
        Directory.CreateDirectory(directory);
        var plist = Path.Combine(directory, Label + ".plist");
        Bootout(plist);
        if (!enabled)
        {
            if (File.Exists(plist)) File.Delete(plist);
            return;
        }
        var escaped = SecurityElement.Escape(path) ?? path;
        File.WriteAllText(plist, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
            <key>Label</key><string>{Label}</string>
            <key>ProgramArguments</key><array><string>{escaped}</string><string>--autostart</string></array>
            <key>RunAtLoad</key><true/>
            </dict></plist>
            """);
        var error = Launchctl($"bootstrap gui/{getuid()} \"{plist}\"");
        if (error.Length > 0) throw new IOException("登录启动未能登记：" + error);
    }, token);

    private static void Bootout(string plist)
    {
        if (File.Exists(plist)) Launchctl($"bootout gui/{getuid()} \"{plist}\"");
    }

    private static string Launchctl(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("/bin/launchctl", arguments)
        {
            UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true
        });
        if (process is null) return "launchctl 未能启动。";
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit(4000);
        return process.ExitCode == 0 ? "" : error;
    }

    [DllImport("libc")] private static extern uint getuid();
}
