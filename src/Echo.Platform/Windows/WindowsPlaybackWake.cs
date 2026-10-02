using System.Runtime.InteropServices;

namespace Echo.Platform.Windows;

/// <summary>Called and released on the same desktop dispatcher thread. Never blocks display sleep.</summary>
public sealed partial class WindowsPlaybackWake : IDisposable
{
    private bool held;
    public void Update(bool playing)
    {
        if (!OperatingSystem.IsWindows() || playing == held) return;
        if (SetThreadExecutionState(playing ? 0x80000001u : 0x80000000u) == 0)
            throw new IOException("无法更新播放时的系统唤醒状态。");
        held = playing;
    }
    public void Dispose() => Update(false);
    [LibraryImport("kernel32.dll")] private static partial uint SetThreadExecutionState(uint flags);
}
