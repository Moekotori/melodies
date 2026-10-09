using System.Runtime.InteropServices;

namespace Echo.Platform.MacOS;

/// <summary>Acknowledges system sleep and reports lid-close. Does not veto sleep.</summary>
public static class MacOsSleepPause
{
    private const uint CanSleep = 0xE0000270;
    private const uint WillSleep = 0xE0000280;
    private static PowerCallback? callback;
    private static Action? onSleep;
    private static uint connection;
    private static nint notifyPort;
    private static uint notifier;

    public static bool Start(Action onWillSleep)
    {
        if (!OperatingSystem.IsMacOS() || callback is not null) return callback is not null;
        try
        {
            onSleep = onWillSleep;
            callback = OnPower;
            var connect = IORegisterForSystemPower(0, out var port, callback, out var note);
            if (connect == 0 || port == 0) { Stop(); return false; }
            connection = connect;
            notifyPort = port;
            notifier = note;
            var source = IONotificationPortGetRunLoopSource(port);
            if (source == 0) { Stop(); return false; }
            var mode = CFStringCreateWithCString(0, "kCFRunLoopDefaultMode", 0x08000100);
            CFRunLoopAddSource(CFRunLoopGetMain(), source, mode);
            if (mode != 0) CFRelease(mode);
            return true;
        }
        catch (Exception)
        {
            Stop();
            return false;
        }
    }

    public static void Stop()
    {
        onSleep = null;
        if (notifier != 0)
        {
            var note = notifier;
            IODeregisterForSystemPower(ref note);
            notifier = 0;
        }
        if (connection != 0)
        {
            IOServiceClose(connection);
            connection = 0;
        }
        if (notifyPort != 0)
        {
            IONotificationPortDestroy(notifyPort);
            notifyPort = 0;
        }
        callback = null;
    }

    private static void OnPower(nint refcon, uint service, uint messageType, nint argument)
    {
        if (messageType == WillSleep)
        {
            try { onSleep?.Invoke(); }
            catch (Exception) { }
        }
        if (messageType is CanSleep or WillSleep && connection != 0) IOAllowPowerChange(connection, argument);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PowerCallback(nint refcon, uint service, uint messageType, nint argument);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern uint IORegisterForSystemPower(nint refcon, out nint port, PowerCallback callback, out uint notifier);
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern int IODeregisterForSystemPower(ref uint notifier);
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern int IOAllowPowerChange(uint connection, nint notificationId);
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern int IOServiceClose(uint connection);
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern nint IONotificationPortGetRunLoopSource(nint port);
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern void IONotificationPortDestroy(nint port);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFRunLoopGetMain();
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRunLoopAddSource(nint loop, nint source, nint mode);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFStringCreateWithCString(nint allocator, string text, uint encoding);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(nint value);
}
