using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Echo.Platform.Steam;

// Discovery only. No Steam client process, API session, or persistent watcher.
public static partial class SteamLibraryLocations
{
    public static IReadOnlyList<string> Find(CancellationToken token)
    {
        var roots = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var candidate in Candidates())
        {
            token.ThrowIfCancellationRequested();
            Add(candidate);
            var manifest = Path.Combine(candidate, "steamapps", "libraryfolders.vdf");
            try
            {
                using var stream = File.OpenRead(manifest);
                if (stream.Length > 512 * 1024) continue;
                using var reader = new StreamReader(stream);
                foreach (Match match in LibraryPath().Matches(reader.ReadToEnd()))
                {
                    token.ThrowIfCancellationRequested();
                    Add(match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\""));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return roots.ToArray();

        void Add(string path)
        {
            if (roots.Count < 64 && Path.IsPathFullyQualified(path) && Directory.Exists(Path.Combine(path, "steamapps")))
                roots.Add(Path.GetFullPath(path));
        }
    }

    private static IEnumerable<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            if (WindowsSteamPath() is { Length: > 0 } registered) yield return registered;
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
                yield return Path.Combine(Environment.GetFolderPath(folder), "Steam");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsMacOS()) yield return Path.Combine(home, "Library", "Application Support", "Steam");
            else
                foreach (var suffix in new[] { ".steam/steam", ".steam/root", ".local/share/Steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam", "snap/steam/common/.local/share/Steam" })
                    yield return Path.Combine(home, suffix);
        }
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; current is not null && depth < 7; depth++, current = current.Parent)
            if (current.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) && current.Parent is { } library)
            { yield return library.FullName; break; }
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") as string;
    }

    [GeneratedRegex("\"(?:path|[0-9]+)\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPath();
}
