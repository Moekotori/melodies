using System.Diagnostics;
using System.IO.Pipes;
using System.Buffers.Binary;
using Echo.Core.Client;
using Echo.Core.Transport;
using Echo.Core.Contracts;
using Echo.Playback;

if (Environment.GetEnvironmentVariable("ECHO_CORE_SMOKE_AUDIO") == "1") { await FakeAudio.RunAsync(); return; }
using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(25));
var token = budget.Token;
var workspace = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var root = Path.Combine(workspace, "misc", "core-ipc-smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var music = Path.Combine(root, "music"); Directory.CreateDirectory(music);
for (var index = 0; index < 128; index++) Wave(Path.Combine(music, index + ".wav"));
var endpoint = new CoreEndpoint(root, Path.Combine(root, "library.sqlite"));
var host = Path.Combine(workspace, "src/Echo.Core.Host/bin/Release/net10.0/Echo.Core.Host.dll");
Environment.SetEnvironmentVariable("ECHO_CORE_HOST", host);
Environment.SetEnvironmentVariable("ECHO_BRIDGE_HOST", Path.Combine(workspace, "src/Echo.Bridge.Host/bin/Release/net10.0/Echo.Bridge.Host.dll"));
Environment.SetEnvironmentVariable("ECHO_AUDIO_HOST", Path.Combine(AppContext.BaseDirectory, "Echo.Core.IpcSmoke" + (OperatingSystem.IsWindows() ? ".exe" : "")));
Environment.SetEnvironmentVariable("ECHO_SMTC_ENABLED", "0");
Environment.SetEnvironmentVariable("ECHO_CORE_SMOKE_AUDIO", "1");
var owned = new HashSet<int>();
CoreConnection? a = null, b = null, c = null, bridge = null;
BridgeClientRuntime? bridgeView = null;
try
{
    Console.WriteLine("IPC_STAGE connect");
    a = await CoreLauncher.ConnectAsync(endpoint, hostPath: host, token: token);
    var info = await a.InfoAsync(token); owned.Add(info.ProcessId);
    Check(info.ProcessId != Environment.ProcessId, "Core did not leave the control process");
    b = await CoreConnection.ConnectAsync(endpoint, token: token);
    Check((await b.InfoAsync(token)).Instance == info.Instance, "Controls opened different owners");
    var session = a.Playback.LocalZone.Session;
    var tracks = await session.ResolveFilesAsync([Path.Combine(music, "0.wav"), Path.Combine(music, "1.wav")], token);
    await session.SetQueueAsync(tracks, tracks[0].ItemId, token);
    await Until(() => b.Playback.LocalZone.Session.Snapshot.Status == PlaybackStatus.Playing, token);
    Check(b.Playback.LocalZone.Session.Queue.Count == 2, "Shared queue was not published");
    await b.Playback.LocalZone.Session.PauseAsync(token);
    await Until(() => a.Playback.LocalZone.Session.Snapshot.Status == PlaybackStatus.Paused, token);
    Console.WriteLine("IPC_STAGE scan-detach");
    var job = await a.Library.StartTaskAsync(new("", Paths: [music]), token);
    await a.DisposeAsync(); a = null;
    Check((await b.InfoAsync(token)).ProcessId == info.ProcessId && b.Playback.LocalZone.Session.Queue.Count == 2, "Detach stopped Core or discarded its queue");
    await UntilAsync(async () => (await b.Library.TasksAsync(token)).Any(task => task.Id == job.Id && task.Status == "completed"), token);
    Check((await b.Library.GetStatsAsync(token)).Tracks == 128, "Accepted scan failed after detaching its control");
    c = await CoreConnection.ConnectAsync(endpoint, token: token);
    var page = await c.Library.GetTracksAsync(new(), token);
    await c.Library.SetTrackFavoriteAsync(page.Items[0].Id, true, token);
    Check((await b.Library.GetTracksAsync(new(), token)).Items.Any(track => track.Id == page.Items[0].Id && track.Favorite), "Clients did not observe committed mutations");
    bridge = await CoreConnection.ConnectAsync(endpoint, "bridge", token);
    try { await bridge.Library.ClearHistoryAsync(token); throw new Exception("Bridge acquired a mutation capability"); }
    catch (CoreRemoteException error) when (error.Code == "forbidden") { }
    try { await bridge.RemoteSources.LoadAsync(token); throw new Exception("Bridge read source credentials"); }
    catch (CoreRemoteException error) when (error.Code == "forbidden") { }
    await bridge.DisposeAsync(); bridge = null;
    Console.WriteLine("IPC_STAGE bridge");
    bridgeView = new(endpoint);
    await bridgeView.Link.StartAsync();
    var port = bridgeView.Link.Port;
    Check(bridgeView.Link.Running && port > 0 && bridgeView.Link.Code.Length > 0, "Independent bridge failed to listen");
    bridgeView.Dispose(); bridgeView = new(endpoint);
    await bridgeView.RefreshAsync(token);
    Check(bridgeView.Link.Running && bridgeView.Link.Port == port, "Closing a bridge control stopped its server");
    await bridgeView.Link.StopAsync(); bridgeView.Dispose(); bridgeView = null;
    var capacity = new List<CoreConnection>();
    try
    {
        for (var index = 0; index < CoreProtocol.MaximumClients - 3; index++) capacity.Add(await CoreConnection.ConnectAsync(endpoint, token: token));
        Check((await b.InfoAsync(token)).Instance == info.Instance, "The last admitted client terminated its owner");
    }
    finally { foreach (var control in capacity) await control.DisposeAsync(); }
    await using (var malformed = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
    {
        await malformed.ConnectAsync(token); var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, int.MaxValue);
        await malformed.WriteAsync(header, token); var response = new byte[1];
        Check(await malformed.ReadAsync(response, token) == 0, "Oversized handshake was not rejected before allocation");
    }
    Check((await b.InfoAsync(token)).Instance == info.Instance, "Malformed client damaged the owner");
    Console.WriteLine("IPC_STAGE backup");
    var backup = Path.Combine(root, "snapshot.zip");
    await b.Backup.CreateAsync(backup, token);
    Check((await b.Backup.ValidateAsync(backup, token)).Files.Any(file => file.Name == "library.sqlite"), "Backup did not use the Core database owner");
    await c.DisposeAsync(); c = null;
    Console.WriteLine("IPC_STAGE shutdown");
    await b.ShutdownAsync(token); await b.DisposeAsync(); b = null;
    using (var process = Process.GetProcessById(info.ProcessId)) await process.WaitForExitAsync(token);
    a = await CoreLauncher.ConnectAsync(endpoint, hostPath: host, token: token);
    var restarted = await a.InfoAsync(token); owned.Add(restarted.ProcessId);
    Check(restarted.Instance != info.Instance && a.Playback.LocalZone.Session.Queue.Count == 2
        && a.Playback.LocalZone.Session.Snapshot.Status == PlaybackStatus.Paused, "Core restart lost the checkpoint or autoplayed");
    await a.ShutdownAsync(token); await a.DisposeAsync(); a = null;
    Console.WriteLine("PASS independent Core/native/Bridge processes, two controls, shared queue, detach-surviving scan, committed mutations, bridge capability restrictions, detached bridge listener, bounded framing, Core-owned backup, persisted restart");
}
finally
{
    bridgeView?.Dispose();
    foreach (var connection in new[] { a, b, c, bridge }) if (connection is not null) await connection.DisposeAsync();
    foreach (var id in owned)
    {
        try { using var process = Process.GetProcessById(id); if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        catch (ArgumentException) { }
    }
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Until(Func<bool> predicate, CancellationToken token) { while (!predicate()) await Task.Delay(20, token); }
static async Task UntilAsync(Func<Task<bool>> predicate, CancellationToken token) { while (!await predicate()) await Task.Delay(50, token); }
static void Wave(string path)
{
    using var writer = new BinaryWriter(File.Create(path)); const int size = 64;
    writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8); writer.Write(16);
    writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000);
    writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(size); writer.Write(new byte[size]);
}
