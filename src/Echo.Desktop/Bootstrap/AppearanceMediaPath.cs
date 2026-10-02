namespace Echo.Desktop.Bootstrap;

internal static class AppearanceMediaPath
{
    public static string Resolve() => Environment.GetEnvironmentVariable("ECHO_APPEARANCE_FFMPEG")
        ?? Path.Combine(AppContext.BaseDirectory, "media", OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
}
