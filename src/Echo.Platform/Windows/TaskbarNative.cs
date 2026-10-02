using System.Runtime.InteropServices;

namespace Echo.Platform.Windows;

// Explicit ITaskbarList3 ABI avoids runtime COM wrappers and remains AOT compatible.
internal static unsafe class TaskbarNative
{
    internal static nint Create(out bool initialized)
    {
        var apartment = CoInitializeEx(0, 2);
        initialized = apartment >= 0;
        if (apartment != unchecked((int)0x80010106)) Check(apartment);
        var clsid = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
        var iid = new Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");
        nint instance = 0;
        try
        {
            Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out instance));
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(instance, 3))(instance)); return instance;
        }
        catch { Release(instance, initialized); initialized = false; throw; }
    }
    internal static void Release(nint instance, bool initialized)
    {
        if (instance != 0) _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(instance, 2))(instance);
        if (initialized) CoUninitialize();
    }
    private static void* Slot(nint instance, int slot) => (void*)(*(nint**)instance)[slot];
    internal static void Buttons(nint instance, nint window, ThumbButton[] buttons, bool add)
    {
        fixed (ThumbButton* pointer = buttons)
            Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, ThumbButton*, int>)Slot(instance, add ? 15 : 16))
                (instance, window, (uint)buttons.Length, pointer));
    }
    internal static void Progress(nint instance, nint window, uint state, ulong position, ulong total)
    {
        Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, int>)Slot(instance, 10))(instance, window, state));
        if (state is 2 or 4 or 8)
            Check(((delegate* unmanaged[Stdcall]<nint, nint, ulong, ulong, int>)Slot(instance, 9))(instance, window, position, total));
    }
    internal static void Tooltip(nint instance, nint window, string title)
    {
        fixed (char* text = title)
            Check(((delegate* unmanaged[Stdcall]<nint, nint, char*, int>)Slot(instance, 19))(instance, window, text));
    }
    internal static void Check(int result)
    { if (result < 0) throw new COMException($"Native taskbar operation failed (0x{result:X8}).", result); }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ThumbButton
    {
        public uint Mask, Id, Bitmap;
        public nint Icon;
        public fixed char Tip[260];
        public uint Flags;
        public static ThumbButton Create(uint id, nint icon, string tooltip, bool enabled, bool visible)
        {
            var value = new ThumbButton { Mask = 2 | 4 | 8, Id = id, Icon = icon,
                Flags = (enabled ? 0u : 1u) | (visible ? 0u : 8u) | (id == 5 ? 2u : 0u) };
            for (var index = 0; index < Math.Min(tooltip.Length, 259); index++) value.Tip[index] = tooltip[index];
            return value;
        }
    }
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint apartment);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessageW(string name);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmInvalidateIconicBitmaps(nint window);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetIconicThumbnail(nint window, nint bitmap, uint flags);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetIconicLivePreviewBitmap(nint window, nint bitmap, nint offset, uint flags);
}
