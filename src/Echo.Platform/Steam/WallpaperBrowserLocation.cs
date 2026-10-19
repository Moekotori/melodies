namespace Echo.Platform.Steam;

public static class WallpaperBrowserLocation
{
    public static string Find()
    {
        var configured = Environment.GetEnvironmentVariable("ECHO_WALLPAPER_BROWSER");
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
        var bundled = Path.Combine(AppContext.BaseDirectory, "wallpaper-runtime", OperatingSystem.IsWindows() ? "chrome.exe" : "chrome");
        if (File.Exists(bundled)) return bundled;
        foreach (var candidate in Candidates()) if (File.Exists(candidate)) return candidate;
        throw new InvalidDataException("动态壁纸需要 Edge、Chrome 或 Chromium 运行时");
    }
    private static IEnumerable<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.LocalApplicationData })
            {
                var basePath = Environment.GetFolderPath(root);
                yield return Path.Combine(basePath, "Microsoft", "Edge", "Application", "msedge.exe");
                yield return Path.Combine(basePath, "Google", "Chrome", "Application", "chrome.exe");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
            yield return "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
            yield return "/Applications/Chromium.app/Contents/MacOS/Chromium";
        }
        else
            foreach (var name in new[] { "chromium", "chromium-browser", "google-chrome", "google-chrome-stable", "microsoft-edge" })
                foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                    if (!string.IsNullOrWhiteSpace(folder)) yield return Path.Combine(folder, name);
    }
    public static string? FindAssets(CancellationToken token)
    {
        var configured = Environment.GetEnvironmentVariable("ECHO_WALLPAPER_ASSETS");
        if (configured is not null && Directory.Exists(configured)) return configured;
        foreach (var root in SteamLibraryLocations.Find(token))
        {
            var assets = Path.Combine(root, "steamapps", "common", "wallpaper_engine", "assets");
            if (Directory.Exists(Path.Combine(assets, "shaders"))) return assets;
        }
        return null;
    }
}
