using System.Runtime.InteropServices;

namespace Echo.Platform.MacOS;

public static partial class MacOSFloatingWindow
{
    public static bool SetPassthrough(nint handle, bool enabled)
    {
        if (handle == 0) return false;
        var target = handle;
        var viewClass = object_getClass(handle);
        var windowSelector = sel_registerName("window");
        if (viewClass != 0 && class_respondsToSelector(viewClass, windowSelector) != 0)
        {
            var window = objc_msgSend(handle, windowSelector);
            if (window != 0) target = window;
        }
        objc_msgSend_bool(target, sel_registerName("setIgnoresMouseEvents:"), enabled ? (byte)1 : (byte)0);
        return (objc_msgSend_bool_ret(target, sel_registerName("ignoresMouseEvents")) != 0) == enabled;
    }

    public static bool TryGetCursor(out int x, out int y)
    {
        x = y = 0;
        var ev = CGEventCreate(0);
        if (ev == 0) return false;
        try
        {
            var point = CGEventGetLocation(ev);
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
            x = (int)Math.Round(point.X);
            y = (int)Math.Round(point.Y);
            return true;
        }
        finally { CFRelease(ev); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MacPoint { public double X; public double Y; }

    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint object_getClass(nint obj);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern byte class_respondsToSelector(nint cls, nint sel);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern nint objc_msgSend(nint receiver, nint selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void objc_msgSend_bool(nint receiver, nint selector, byte value);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern byte objc_msgSend_bool_ret(nint receiver, nint selector);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern nint CGEventCreate(nint source);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern MacPoint CGEventGetLocation(nint ev);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(nint cf);
}
