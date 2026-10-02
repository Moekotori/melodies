using System.Runtime.InteropServices;

namespace Echo.Platform.Windows;

internal static class WindowsShortcutNative
{
    internal delegate nint MouseHook(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Message
    { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseData
    { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] internal static extern int GetMessageW(out Message message, nint window, uint first, uint last);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PeekMessageW(out Message message, nint window, uint first, uint last, uint remove);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookExW(int id, MouseHook callback, nint module, uint thread);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
}
