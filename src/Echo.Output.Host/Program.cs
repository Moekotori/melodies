using System.Net;
using Echo.Core.Transport;
using Echo.Output.Host;

var values = new Dictionary<string, string>(StringComparer.Ordinal);
for (var index = 0; index < args.Length; index++)
{
    if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length || !values.TryAdd(args[index][2..], args[++index])) return 2;
}
if (values.Keys.Any(key => key is not ("data" or "listen" or "native-host" or "media-connection" or "pair-controller" or "host" or "port" or "revoke-controller"))) return 2;
try
{
    var directory = Path.GetFullPath(values.GetValueOrDefault("data") ?? Path.Combine(CoreEndpoint.DefaultDirectory, "output"));
    Directory.CreateDirectory(directory);
    var endpoint = new CoreEndpoint(directory, Path.Combine(directory, "output.identity"), "output");
    if (values.TryGetValue("pair-controller", out var pairing))
    {
        if (!values.TryGetValue("host", out var host) || !int.TryParse(values.GetValueOrDefault("port"), out var port)) return 2;
        RemoteCoreAccess.Pair(endpoint, host, port, pairing, "output-controller");
        Console.WriteLine($"Output controller paired: {RemoteCoreProfile.Load(pairing).ClientId}. Transfer the private pairing file to Core."); return 0;
    }
    if (values.TryGetValue("revoke-controller", out var revoked)) { RemoteCoreAccess.Revoke(endpoint, Guid.Parse(revoked)); return 0; }
    if (!IPEndPoint.TryParse(values.GetValueOrDefault("listen") ?? "", out var address) || address.Port == 0) return 2;
    var media = RemoteCoreProfile.Load(values["media-connection"]);
    if (media.Role != "output-media") return 2;
    await using var owner = new FileStream(Path.Combine(directory, "output.owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
    using var access = RemoteCoreAccess.Open(endpoint);
    await using var worker = new OutputWorker(Path.GetFullPath(values["native-host"]), media, endpoint.Identity);
    await using var server = new CoreServer(endpoint, worker, "output-controller", reliableNotices: true);
    var watching = worker.WatchAsync(server, stop.Token);
    try { await server.RunRemoteAsync(address, access, stop.Token); }
    finally { stop.Cancel(); await watching; }
    return 0;
}
catch (OperationCanceledException) { return 0; }
catch (Exception) { Console.Error.WriteLine("ECHO Output could not start or lost its native session. Check owner configuration."); return 1; }
