namespace Echo.Desktop.Bootstrap;

internal static class AppearanceMediaPath
{
    public static string Resolve() => Environment.GetEnvironmentVariable("ECHO_APPEARANCE_FFMPEG")
        ?? Path.Combine(AppContext.BaseDirectory, "media", OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

    public static string AnalysisHost() => Environment.GetEnvironmentVariable("ECHO_TRACK_ANALYSIS")
        ?? Path.Combine(AppContext.BaseDirectory, "native", OperatingSystem.IsWindows() ? "echo-track-analysis.exe" : "echo-track-analysis");
}
