using Avalonia.Media;

namespace Echo.Desktop.Bootstrap;

public static class DesktopFonts
{
    public static FontManagerOptions CreateOptions() => new()
    {
        FontFallbacks = [new FontFallback
        {
            FontFamily = new FontFamily(OperatingSystem.IsWindows() ? "Microsoft YaHei" : "Noto Sans CJK SC, PingFang SC")
        }]
    };
}
