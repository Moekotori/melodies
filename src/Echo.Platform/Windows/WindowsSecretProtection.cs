using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Echo.Platform.Windows;

public static class WindowsSecretProtection
{
    public static byte[] Protect(byte[] input) => Transform(input, true);
    public static byte[] Unprotect(byte[] input) => Transform(input, false);
    private static byte[] Transform(byte[] input, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("此平台尚未提供本机凭据保护。");
        var source = new Blob { Length = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        Blob result = default;
        try
        {
            Marshal.Copy(input, 0, source.Data, input.Length);
            var success = protect ? CryptProtectData(ref source, "ECHO Avalonia", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out result)
                : CryptUnprotectData(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out result);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "本机凭据保护失败。");
            var bytes = new byte[result.Length]; Marshal.Copy(result.Data, bytes, 0, result.Length); return bytes;
        }
        finally
        {
            Marshal.Copy(new byte[source.Length], 0, source.Data, source.Length); Marshal.FreeHGlobal(source.Data);
            if (result.Data != IntPtr.Zero) { Marshal.Copy(new byte[result.Length], 0, result.Data, result.Length); LocalFree(result.Data); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob data, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob result);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob data, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob result);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
