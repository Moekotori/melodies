using System.Diagnostics;
using System.Text.Json;
using Echo.Core.Transport;
namespace Echo.Core.Host.Composition;

internal static class BridgeStartup
{
    public static void RestoreIfEnabled(CoreEndpoint endpoint)
    {
        try
        {
            var path = Path.Combine(endpoint.Directory, "bridge-settings.json");
            var legacy = !File.Exists(path);
            if (legacy) path = Path.Combine(endpoint.Directory, "preferences.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 256 * 1024) return;
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var settings = document.RootElement;
            if (legacy && !settings.TryGetProperty("features", out settings)) return;
            var enabled = settings.EnumerateObject().Take(256).Any(pair => pair.Name is "connect.receive" or "connect.link" or "connect.mqtt"
                && pair.Value.TryGetProperty("enabled", out var value) && value.ValueKind == JsonValueKind.True);
            if (!enabled) return;
            var host = ServiceExecutable.Resolve(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "bridge")), "Echo.Bridge.Host", "ECHO_BRIDGE_HOST");
            if (!File.Exists(host)) return;
            var start = new ProcessStartInfo(Path.GetExtension(host) == ".dll" ? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet" : host)
                { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetExtension(host) == ".dll") start.ArgumentList.Add(host);
            foreach (var value in new[] { "--data", endpoint.Directory, "--db", endpoint.LibraryPath }) start.ArgumentList.Add(value);
            using var process = Process.Start(start);
        }
        catch (Exception) { /* An explicit Bridge action exposes a failed launch without blocking Core startup. */ }
    }
}
