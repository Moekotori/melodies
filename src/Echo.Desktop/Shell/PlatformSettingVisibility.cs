namespace Echo.Desktop.Shell;

internal static class PlatformSettingVisibility
{
    private static readonly HashSet<string> MacOsHidden =
    [
        "close-to-tray", "touch-keyboard", "smtc", "smtc-lyrics", "steam-leaderboards",
        "taskbar-mini-player", "taskbar-mini-player-advanced", "taskbar-progress-enabled", "taskbar-playback"
    ];

    public static bool IsAvailable(string id) => !OperatingSystem.IsMacOS() || !MacOsHidden.Contains(id);
}
