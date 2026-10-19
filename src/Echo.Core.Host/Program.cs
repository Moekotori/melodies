using Echo.Core.Host.Composition;
using Echo.Core.Hosting;
using Echo.Core.Transport;

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var index = 0; index < args.Length; index++)
{
    if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length || !options.TryAdd(args[index][2..], args[++index])) return 2;
}
if (options.Keys.Any(key => key is not ("data" or "db" or "native-root" or "persistent" or "media" or "idle-seconds"))) return 2;
var directory = Path.GetFullPath(options.GetValueOrDefault("data") ?? CoreEndpoint.Default.Directory);
var endpoint = new CoreEndpoint(directory, Path.GetFullPath(options.GetValueOrDefault("db") ?? Path.Combine(directory, "library.sqlite")));
Directory.CreateDirectory(Path.GetDirectoryName(endpoint.LibraryPath)!);
Directory.CreateDirectory(directory);
var ownershipPath = endpoint.LibraryPath + ".core.lock";
FileStream ownership;
try { ownership = new(ownershipPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
catch (IOException) { return 0; } // Another launch owns this library. Clients connect to its handshake instead.
await using (ownership)
{
    FileStream profile;
    try { profile = new(Path.Combine(directory, "core.profile.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    catch (IOException) { return 0; }
    await using var profileOwnership = profile;
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(ownershipPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
    try
    {
        var native = Path.GetFullPath(options.GetValueOrDefault("native-root") ?? Path.Combine(AppContext.BaseDirectory, "native"));
        await using var runtime = HostComposition.Create(endpoint, native, options.GetValueOrDefault("persistent") != "0", options.GetValueOrDefault("media") != "0");
        await runtime.StartAsync(shutdown.Token);
        // Claim the existing writer lock before advertising readiness, including when no Control has browsed yet.
        _ = await runtime.Library.GetTracksAsync(new(Limit: 1), shutdown.Token);
        BridgeStartup.RestoreIfEnabled(endpoint);
        var idleSeconds = int.TryParse(options.GetValueOrDefault("idle-seconds"), out var configured) ? Math.Clamp(configured, 2, 600) : 60;
        await CoreServiceHost.RunAsync(runtime, endpoint, TimeSpan.FromSeconds(idleSeconds), shutdown.Token);
        return 0;
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { return 0; }
    catch (Exception) { Console.Error.WriteLine("ECHO Core could not start or stopped after a service fault."); return 1; }
}
