using System.Runtime.InteropServices;

namespace Echo.Infrastructure.Metadata;

internal static class NativeScannerLocator
{
    public static string? Find()
    {
        var name = OperatingSystem.IsWindows() ? "echo-native-scanner.exe" : "echo-native-scanner";
        foreach (var candidate in new[] { Path.Combine(AppContext.BaseDirectory, name),
            Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", name) })
            if (File.Exists(candidate)) return candidate;
        return null;
    }
}
