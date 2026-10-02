using System.Runtime.InteropServices;

namespace Echo.Platform.Windows;

internal static class TaskbarBitmap
{
    internal static nint Create(byte[] pixels, int width, int height)
    {
        if (width is <= 0 or > 512 || height is <= 0 or > 512 || pixels.Length != width * height * 4)
            throw new ArgumentException("Invalid taskbar bitmap dimensions.");
        var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var bitmap = CreateDIBSection(0, ref info, 0, out var pointer, 0, 0);
        if (bitmap == 0) throw new InvalidOperationException("Cannot allocate taskbar bitmap.");
        Marshal.Copy(pixels, 0, pointer, pixels.Length);
        return bitmap;
    }
    internal static nint Icon(byte[] pixels, int size)
    {
        var color = Create(pixels, size, size);
        var mask = CreateBitmap(size, size, 1, 1, new byte[((size + 15) / 16) * 2 * size]);
        try
        {
            var info = new IconInfo { IsIcon = 1, Color = color, Mask = mask };
            var icon = CreateIconIndirect(ref info);
            if (icon == 0) throw new InvalidOperationException("Cannot allocate taskbar icon.");
            return icon;
        }
        finally { DeleteObject(color); if (mask != 0) DeleteObject(mask); }
    }
    // DWM requests vary with shell settings/DPI. Fit a square without exceeding its bounds.
    internal static byte[] Scale(byte[] source, int sourceSize, int size)
    {
        if (size == sourceSize) return source;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var from = ((y * sourceSize / size) * sourceSize + x * sourceSize / size) * 4;
                var to = (y * size + x) * 4;
                pixels[to] = source[from]; pixels[to + 1] = source[from + 1];
                pixels[to + 2] = source[from + 2]; pixels[to + 3] = source[from + 3];
            }
        return pixels;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPixels, YPixels;
        public uint ColorsUsed, Important;
        public uint Color;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo { public int IsIcon; public uint XHotspot, YHotspot; public nint Mask, Color; }
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint CreateBitmap(int width, int height, uint planes, uint bits, byte[] pixels);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll")] private static extern nint CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint handle);
}
