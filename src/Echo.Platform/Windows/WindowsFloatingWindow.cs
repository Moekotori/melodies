using System.Runtime.InteropServices;

namespace Echo.Platform.Windows;

public static partial class WindowsFloatingWindow
{
    private const int ExtendedStyle = -20;
    private const int Transparent = 0x20, Layered = 0x80000, NoActivate = 0x8000000;
    public static bool SetPassthrough(nint handle, bool enabled)
    {
        if (handle == 0) return false;
        var style = GetWindowLongW(handle, ExtendedStyle);
        Marshal.SetLastPInvokeError(0);
        var next = enabled ? style | Transparent | Layered | NoActivate : style & ~Transparent;
        var accepted = SetWindowLongW(handle, ExtendedStyle, next) != 0 || Marshal.GetLastPInvokeError() == 0;
        return accepted && ((GetWindowLongW(handle, ExtendedStyle) & Transparent) != 0) == enabled;
    }
    public static bool TryGetCursor(out int x, out int y)
    {
        if (!GetCursorPos(out var point)) { x = y = 0; return false; }
        x = point.X; y = point.Y; return true;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WinPoint { public int X; public int Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLongW(nint handle, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLongW(nint handle, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out WinPoint point);
}
