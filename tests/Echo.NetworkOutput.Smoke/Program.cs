using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Echo.Core.Client;
using Echo.Core.Contracts;
using Echo.Core.Transport;
using Echo.Output;
using Echo.Playback;

if (Environment.GetEnvironmentVariable("ECHO_OUTPUT_SMOKE_NATIVE") == "1") { await OutputFakeAudio.RunAsync(args); return; }
using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(35)); var token = limit.Token;
var workspace = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var root = Path.Combine(workspace, "misc", "output-smoke", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
using var cleanup = new FixtureDirectory(root);
var coreRoot = Path.Combine(root, "core"); Directory.CreateDirectory(coreRoot);
var outputRoot = Path.Combine(root, "output"); Directory.CreateDirectory(outputRoot);
var song = Path.Combine(coreRoot, "song.wav"); Wave(song);
var coreEndpoint = new CoreEndpoint(coreRoot, Path.Combine(coreRoot, "library.sqlite"));
var outputEndpoint = new CoreEndpoint(outputRoot, Path.Combine(outputRoot, "output.identity"), "output");
var mediaPort = Port(); var outputPort = Port();
var controllerFile = Path.Combine(root, "controller.echo-core-client.json");
var mediaFile = Path.Combine(root, "media.echo-core-client.json");
RemoteCoreAccess.Pair(outputEndpoint, "127.0.0.1", outputPort, controllerFile, "output-controller");
RemoteCoreAccess.Pair(coreEndpoint, "127.0.0.1", mediaPort, mediaFile, "output-media");
var controller = RemoteCoreProfile.Load(controllerFile); var mediaProfile = RemoteCoreProfile.Load(mediaFile);
var fake = Path.Combine(AppContext.BaseDirectory, "Echo.NetworkOutput.Smoke" + (OperatingSystem.IsWindows() ? ".exe" : ""));
var pidFile = Path.Combine(outputRoot, "native.pid");
var outputStart = Start("Echo.Output.Host", "--data", outputRoot, "--listen", "127.0.0.1:" + outputPort, "--native-host", fake, "--media-connection", mediaFile);
outputStart.Environment["ECHO_OUTPUT_SMOKE_NATIVE"] = "1"; outputStart.Environment["ECHO_OUTPUT_SMOKE_PID"] = pidFile;
outputStart.Environment["ECHO_OUTPUT_SMOKE_HASH"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(song)));
using var output = Process.Start(outputStart) ?? throw new Exception("Output launch failed");
Process? coreProcess = null; CoreConnection? control = null; var nativeId = 0;
try
{
    Console.WriteLine("OUTPUT_STAGE permissions");
    CoreWireClient? peer = null;
    while (peer is null)
    {
        try { peer = await CoreWireClient.ConnectRemoteAsync(controller, token); }
        catch (SocketException) when (!token.IsCancellationRequested) { await Task.Delay(40, token); }
    }
    await using (peer)
    {
        peer.Start();
        var ready = OutputJson.Read<OutputReady>(await peer.CallAsync("output.start", OutputJson.Value(new OutputStart(1, 2)), token));
        using var forged = JsonDocument.Parse("{\"source\":{\"kind\":\"local\",\"uri\":\"/must-not-open\"}}");
        try { await peer.CallAsync("output.command", OutputJson.Value(new OutputCommand(ready.Generation, "audio.openSource", forged.RootElement)), token); throw new Exception("Output accepted a local path"); }
        catch (CoreRemoteException error) when (error.Code == "forbidden") { }
    }
    Console.WriteLine("OUTPUT_STAGE core");
    var coreStart = Start("Echo.Core.Host", "--data", coreRoot, "--db", coreEndpoint.LibraryPath, "--media", "0", "--output-connection", controllerFile,
        "--output-media-listen", "127.0.0.1:" + mediaPort, "--output-media-client-id", mediaProfile.ClientId.ToString());
    coreStart.Environment["ECHO_AUDIO_HOST"] = Path.Combine(root, "local-native-must-not-start");
    coreProcess = Process.Start(coreStart) ?? throw new Exception("Core launch failed");
    while (control is null)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token); attempt.CancelAfter(400);
        try { control = await CoreConnection.ConnectAsync(coreEndpoint, token: attempt.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
    }
    var session = control.Playback.LocalZone.Session;
    var tracks = await session.ResolveFilesAsync([song], token);
    await session.SetQueueAsync(tracks, tracks[0].ItemId, token);
    await Until(() => session.Snapshot.Status == PlaybackStatus.Playing, token);
    Check(control.Playback.LocalZone.Name == "网络 Output", "Network output was presented as a local device");
    Check(session.Snapshot.Current?.FilePath == song, "Output proxy URI leaked into Core identity");
    nativeId = int.Parse(File.ReadAllText(pidFile));
    Check(nativeId != coreProcess.Id && nativeId != output.Id, "Native process ownership was lost");
    var devices = await session.GetOutputDevicesAsync("shared", token);
    Check(devices.Count == 1 && devices[0].Name == "Output smoke device", "Output device discovery ran on the wrong machine");
    await session.PauseAsync(token); Check(session.Snapshot.Status == PlaybackStatus.Paused, "Pause was not confirmed");
    await session.ResumeAsync(token); await Until(() => session.Snapshot.Status == PlaybackStatus.Playing, token);
    Console.WriteLine("OUTPUT_STAGE owner-and-media");
    try { await using var second = await CoreWireClient.ConnectRemoteAsync(controller, token); throw new Exception("Two Core owners acquired one Output"); }
    catch (IOException) { }
    await using (var stream = await RemoteCoreTransport.ConnectAsync(mediaProfile, token))
    {
        var denied = new OutputMediaRequest(1, coreEndpoint.Identity, mediaProfile.ClientId, mediaProfile.AccessToken, new string('A', 64), 0, 1);
        await CoreFrames.WriteAsync(stream, new("output.media", Payload: OutputJson.Value(denied)), token);
        Check(await CoreFrames.ReadAsync(stream, token) is null, "Unknown file capability was accepted");
    }
    try { await using var invalid = await CoreConnection.ConnectRemoteAsync(controller with { Role = "remote-control" }, token); throw new Exception("Output pairing became a Control pairing"); }
    catch (CoreRemoteException) { }
    await control.DisposeAsync(); control = null;
    control = await CoreConnection.ConnectAsync(coreEndpoint, token: token);
    Check(control.Playback.LocalZone.Session.Snapshot.Status == PlaybackStatus.Playing, "Control detach stopped independent Output");
    Console.WriteLine("OUTPUT_STAGE core-loss");
    coreProcess.Kill(true); await coreProcess.WaitForExitAsync(token);
    await Until(() => !Alive(nativeId), token);
    Check(!output.HasExited, "Output service was coupled to Core lifetime");
    await control.DisposeAsync(); control = null; coreProcess.Dispose();
    Console.WriteLine("OUTPUT_STAGE persisted-binding");
    var restart = Start("Echo.Core.Host", "--data", coreRoot, "--db", coreEndpoint.LibraryPath, "--media", "0");
    restart.Environment["ECHO_AUDIO_HOST"] = Path.Combine(root, "local-native-must-not-start");
    coreProcess = Process.Start(restart) ?? throw new Exception("Core restart failed");
    while (control is null)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token); attempt.CancelAfter(400);
        try { control = await CoreConnection.ConnectAsync(coreEndpoint, token: attempt.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        catch (IOException) when (!token.IsCancellationRequested) { await Task.Delay(40, token); }
    }
    Check(control.Playback.LocalZone.Name == "网络 Output", "Automatic Core launch lost the output binding");
    session = control.Playback.LocalZone.Session;
    Check(session.Snapshot.Status != PlaybackStatus.Playing, "Core replayed audio after a crash without a request");
    tracks = await session.ResolveFilesAsync([song], token); await session.SetQueueAsync(tracks, tracks[0].ItemId, token);
    await Until(() => session.Snapshot.Status == PlaybackStatus.Playing, token);
    nativeId = int.Parse(File.ReadAllText(pidFile));
    await control.ShutdownAsync(token); await coreProcess.WaitForExitAsync(token); await Until(() => !Alive(nativeId), token);
    Console.WriteLine("PASS separate Core/Output/native processes, protected binary file + Range/hash verification, device discovery, confirmed play/pause/resume, source identity, single Core lease, capability and role rejection, Control detach and native release after Core loss");
}
finally
{
    if (control is not null) await control.DisposeAsync();
    if (coreProcess is not null) { if (!coreProcess.HasExited) { coreProcess.Kill(true); await coreProcess.WaitForExitAsync(); } coreProcess.Dispose(); }
    if (!output.HasExited) { output.Kill(true); await output.WaitForExitAsync(); }
}
ProcessStartInfo Start(string project, params string[] arguments)
{
    var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false };
    start.ArgumentList.Add(Path.Combine(workspace, "src", project, "bin/Release/net10.0", project + ".dll"));
    foreach (var argument in arguments) start.ArgumentList.Add(argument); return start;
}
static int Port() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
static bool Alive(int id) { try { using var process = Process.GetProcessById(id); return !process.HasExited; } catch (ArgumentException) { return false; } }
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static async Task Until(Func<bool> condition, CancellationToken token) { while (!condition()) await Task.Delay(30, token); }
static void Wave(string path)
{
    using var writer = new BinaryWriter(File.Create(path)); const int size = 1024 * 1024 + 4096;
    writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8); writer.Write(16);
    writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000);
    writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(size);
    for (var index = 0; index < size; index++) writer.Write((byte)((index * 31 + 7) & 255));
}
sealed class FixtureDirectory(string path) : IDisposable { public void Dispose() { if (Directory.Exists(path)) Directory.Delete(path, true); } }
