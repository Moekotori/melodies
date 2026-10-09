using System.Runtime.InteropServices;
using System.Text;

namespace Echo.Platform.MacOS;

/// <summary>Reads Reduce Motion. One query, no observer.</summary>
public static class MacOsMotion
{
    public static bool? AllowsAnimation()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        EnsureAppKit();
        var workspace = objc_msgSend(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
        if (workspace == 0) return null;
        return objc_msgSend_bool(workspace, sel_registerName("accessibilityDisplayShouldReduceMotion")) == 0;
    }

    internal static void EnsureAppKit() => dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", 9);

    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern nint dlopen(string path, int mode);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern nint objc_msgSend(nint receiver, nint selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern byte objc_msgSend_bool(nint receiver, nint selector);
}

/// <summary>Dock badge and one cover. AppKit calls stay on the UI thread.</summary>
public static class MacOsDockTile
{
    public static void SetBadge(string? label)
    {
        if (!OperatingSystem.IsMacOS()) return;
        MacOsMotion.EnsureAppKit();
        var tile = DockTile();
        if (tile == 0) return;
        objc_msgSend(tile, sel_registerName("setBadgeLabel:"), label is { Length: > 0 } ? NsString(label) : 0);
    }

    public static void SetIcon(string? path)
    {
        if (!OperatingSystem.IsMacOS()) return;
        MacOsMotion.EnsureAppKit();
        var app = SharedApplication();
        if (app == 0) return;
        nint image = 0;
        if (path is { Length: > 0 and <= 1024 })
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length is > 0 and <= 6_000_000)
                    image = objc_msgSend(objc_msgSend(objc_getClass("NSImage"), sel_registerName("alloc")), sel_registerName("initWithContentsOfFile:"), NsString(path));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { image = 0; }
        }
        objc_msgSend(app, sel_registerName("setApplicationIconImage:"), image);
        if (image != 0) objc_msgSend(image, sel_registerName("release"));
    }

    private static nint SharedApplication() => objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
    private static nint DockTile()
    {
        var app = SharedApplication();
        return app == 0 ? 0 : objc_msgSend(app, sel_registerName("dockTile"));
    }

    private static unsafe nint NsString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var buffer = stackalloc byte[bytes.Length + 1];
        for (var i = 0; i < bytes.Length; i++) buffer[i] = bytes[i];
        buffer[bytes.Length] = 0;
        return objc_msgSend(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), (nint)buffer);
    }

    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern nint sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern nint objc_msgSend(nint receiver, nint selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern nint objc_msgSend(nint receiver, nint selector, nint arg);
}
