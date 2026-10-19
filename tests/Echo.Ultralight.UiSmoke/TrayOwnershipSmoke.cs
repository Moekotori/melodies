using Avalonia;
using Avalonia.Controls;
using Echo.Desktop.Shell;

internal static class TrayOwnershipSmoke
{
    public static void Run(MainWindow window, ShellViewModel shell)
    {
        var application = Application.Current!;
        var originalCount = TrayIcon.GetIcons(application)?.Count ?? 0;
        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("Platform playback controls"));
        var icon = window.SetPlatformTrayMenu(menu)!;
        Require(TrayIcon.GetIcons(application)!.Count == originalCount + 1 && ReferenceEquals(icon.Menu, menu),
            "platform menu uses a single residency-owned icon");
        shell.UltraLightEnabled = true;
        window.EnsureTrayRecovery();
        Require(TrayIcon.GetIcons(application)!.Count == originalCount + 1 && ReferenceEquals(icon.Menu, menu),
            "Ultralight reuses the platform icon and keeps its menu");
        Require(ReferenceEquals(window.SetPlatformTrayMenu(null), icon) && !ReferenceEquals(icon.Menu, menu),
            "removing platform presentation restores the ordinary recovery menu on the same icon");
        Require(ReferenceEquals(window.SetPlatformTrayMenu(menu), icon), "reattaching the platform menu reuses the icon");
        shell.UltraLightEnabled = false;
        Require(TrayIcon.GetIcons(application)!.Count == originalCount + 1 && ReferenceEquals(icon.Menu, menu),
            "platform recovery remains available with Ultralight disabled");
        window.SetPlatformTrayMenu(null);
        Require(TrayIcon.GetIcons(application)!.Count == originalCount, "the unused shared icon is released");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
