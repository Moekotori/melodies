using Echo.Core.Host.Composition;
using Echo.Core.Hosting;
using Echo.Core.Transport;
using System.Net;

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var index = 0; index < args.Length; index++)
{
    if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length || !options.TryAdd(args[index][2..], args[++index])) return 2;
}
if (options.Keys.Any(key => key is not ("data" or "db" or "native-root" or "persistent" or "media" or "idle-seconds" or "remote-listen" or "pair-client" or "remote-host" or "remote-port" or "revoke-client" or "pair-output-media" or "output-connection" or "output-media-listen" or "output-media-client-id" or "local-output"))) return 2;
var directory = Path.GetFullPath(options.GetValueOrDefault("data") ?? CoreEndpoint.Default.Directory);
var endpoint = new CoreEndpoint(directory, Path.GetFullPath(options.GetValueOrDefault("db") ?? Path.Combine(directory, "library.sqlite")));
Directory.CreateDirectory(Path.GetDirectoryName(endpoint.LibraryPath)!);
Directory.CreateDirectory(directory);
if (options.ContainsKey("pair-client") || options.ContainsKey("pair-output-media") || options.ContainsKey("revoke-client"))
{
    try
    {
        if (new[] { "pair-client", "pair-output-media", "revoke-client" }.Count(options.ContainsKey) != 1) return 2;
        var mediaPairing = options.ContainsKey("pair-output-media");
        if (options.TryGetValue(mediaPairing ? "pair-output-media" : "pair-client", out var pairing))
        {
            if (!options.TryGetValue("remote-host", out var host) || !int.TryParse(options.GetValueOrDefault("remote-port"), out var port)) return 2;
            RemoteCoreAccess.Pair(endpoint, host, port, pairing, mediaPairing ? "output-media" : "remote-control");
            Console.WriteLine($"{(mediaPairing ? "Output media" : "Core control")} paired: {RemoteCoreProfile.Load(pairing).ClientId}. Pairing file contains a secret; transfer it privately.");
        }
        else RemoteCoreAccess.Revoke(endpoint, Guid.Parse(options["revoke-client"]));
        return 0;
    }
    catch (Exception) { Console.Error.WriteLine("Core pairing update failed. Check the owner files and arguments."); return 1; }
}
IPEndPoint? remoteAddress = null;
if (options.TryGetValue("remote-listen", out var bind) && (!IPEndPoint.TryParse(bind, out remoteAddress) || remoteAddress.Port == 0)) return 2;
if (options.ContainsKey("remote-host") || options.ContainsKey("remote-port")) return 2;
var ownershipPath = endpoint.LibraryPath + ".core.lock";
FileStream ownership;
try { ownership = new(ownershipPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
catch (IOException)
{
    if (remoteAddress is not null) { Console.Error.WriteLine("Core already runs; restart its owner to change network listeners."); return 3; }
    return 0;
}
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
        using var remoteAccess = remoteAddress is null ? null : RemoteCoreAccess.Open(endpoint);
        var output = CoreOutputOptions.Parse(options, endpoint);
        var native = Path.GetFullPath(options.GetValueOrDefault("native-root") ?? Path.Combine(AppContext.BaseDirectory, "native"));
        await using var runtime = HostComposition.Create(endpoint, native, options.GetValueOrDefault("persistent") != "0", options.GetValueOrDefault("media") != "0", output);
        await runtime.StartAsync(shutdown.Token);
        // Claim the existing writer lock before advertising readiness, including when no Control has browsed yet.
        _ = await runtime.Library.GetTracksAsync(new(Limit: 1), shutdown.Token);
        BridgeStartup.RestoreIfEnabled(endpoint);
        var idleSeconds = int.TryParse(options.GetValueOrDefault("idle-seconds"), out var configured) ? Math.Clamp(configured, 2, 600) : 60;
        using var metadata = endpoint.Identity == CoreEndpoint.Default.Identity
            ? new CoreMetadataEndpoint(Path.Combine(directory, "core")) : null;
        await CoreServiceHost.RunAsync(runtime, endpoint, TimeSpan.FromSeconds(idleSeconds), shutdown.Token, remoteAddress, remoteAccess,
            metadata is null ? null : metadata.RunAsync, () => metadata?.HasActivity == true);
        return 0;
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { return 0; }
    catch (Exception) { Console.Error.WriteLine("ECHO Core could not start or stopped after a service fault."); return 1; }
}
