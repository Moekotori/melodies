using Echo.Bridge;
using Echo.Bridge.Host;
using Echo.Core.Client;
using Echo.Core.Transport;

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var index = 0; index < args.Length; index++)
{
    if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length || !options.TryAdd(args[index][2..], args[++index])) return 2;
}
if (options.Keys.Any(key => key is not ("data" or "db"))) return 2;
var directory = Path.GetFullPath(options.GetValueOrDefault("data") ?? CoreEndpoint.Default.Directory);
var endpoint = new CoreEndpoint(directory, Path.GetFullPath(options.GetValueOrDefault("db") ?? Path.Combine(directory, "library.sqlite")));
Directory.CreateDirectory(Path.GetDirectoryName(endpoint.LibraryPath)!);
FileStream ownership;
try { ownership = new(endpoint.LibraryPath + ".bridge.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
catch (IOException) { return 0; }
await using (ownership)
{
    using var lifetime = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
    try
    {
        await using var core = await CoreLauncher.ConnectAsync(endpoint, role: "bridge", hostPath: ServiceExecutable.Resolve(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "core")), "Echo.Core.Host", "ECHO_CORE_HOST"), token: lifetime.Token);
        using var service = new BridgeService(core.Playback.LocalZone.Session, new BridgePreferencesStore(directory));
        using var handler = new BridgeHandler(service, core.Playback.LocalZone.Session);
        await using var server = new CoreServer(endpoint with { Service = "bridge" }, handler);
        await service.ExecuteAsync(new("restore"), lifetime.Token);
        var idle = Task.Run(async () =>
        {
            var since = Environment.TickCount64;
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(lifetime.Token))
                {
                    if (server.ClientCount > 0 || service.Active) since = Environment.TickCount64;
                    else if (Environment.TickCount64 - since >= 60000) { lifetime.Cancel(); break; }
                    if (!core.Playback.LocalZone.Session.State.IsAvailable) { lifetime.Cancel(); break; }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        });
        try { await server.RunAsync(lifetime.Token); }
        finally { lifetime.Cancel(); await idle; }
        return 0;
    }
    catch (Exception) { Console.Error.WriteLine("ECHO Bridge could not start or lost its Core connection."); return 1; }
}
