using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Echo.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class WindowsAnimationPreference
{
    // Cheap OS preference query; no registry/file I/O or polling loop.
    public static bool? Read()
    {
        var enabled = 0;
        return SystemParametersInfoW(0x1042, 0, ref enabled, 0) ? enabled != 0 : null;
    }
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, ref int value, uint flags);
}
