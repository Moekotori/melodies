using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Echo.Core.Client;
using Echo.Core.Contracts;
using Echo.Core.Transport;
using Echo.Playback;

if (Environment.GetEnvironmentVariable("ECHO_CORE_SMOKE_AUDIO") == "1") { await FakeAudio.RunAsync(); return; }
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var token = deadline.Token;
var workspace = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var root = Path.Combine(workspace, "misc", "core-remote-smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
using var cleanup = new TestDirectory(root);
var music = Path.Combine(root, "music"); Directory.CreateDirectory(music);
var song = Path.Combine(music, "song.wav"); Wave(song);
var endpoint = new CoreEndpoint(root, Path.Combine(root, "library.sqlite"));
var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
var pairing = Path.Combine(root, "control.echo-core-client.json");
RemoteCoreAccess.Pair(endpoint, "127.0.0.1", port, pairing);
// Previous remote-control pairing files had no role field; preserve their constructor default.
File.WriteAllText(pairing, File.ReadAllText(pairing).Replace(",\"role\":\"remote-control\"", "", StringComparison.Ordinal));
var grantsFile = Path.Combine(root, "remote-access", "clients.json");
File.WriteAllText(grantsFile, File.ReadAllText(grantsFile).Replace(",\"role\":\"remote-control\"", "", StringComparison.Ordinal));
var profile = RemoteCoreProfile.Load(pairing);
Check(!profile.ToString().Contains(profile.AccessToken, StringComparison.Ordinal), "Pairing secret exposed by diagnostics");
if (!OperatingSystem.IsWindows()) Check((File.GetUnixFileMode(pairing) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "Pairing file is not private");
var host = Path.Combine(workspace, "src/Echo.Core.Host/bin/Release/net10.0/Echo.Core.Host.dll");
var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false };
foreach (var arg in new[] { host, "--data", root, "--db", endpoint.LibraryPath, "--media", "0", "--remote-listen", "127.0.0.1:" + port }) start.ArgumentList.Add(arg);
start.Environment["ECHO_CORE_SMOKE_AUDIO"] = "1";
start.Environment["ECHO_AUDIO_HOST"] = Path.Combine(AppContext.BaseDirectory, "Echo.Core.RemoteSmoke" + (OperatingSystem.IsWindows() ? ".exe" : ""));
using var process = Process.Start(start) ?? throw new IOException("Core launch failed");
CoreConnection? local = null, remote = null;
try
{
    Console.WriteLine("REMOTE_STAGE connect");
    while (local is null)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token); attempt.CancelAfter(400);
        try { local = await CoreConnection.ConnectAsync(endpoint, token: attempt.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
    }
    await local.Library.ImportAsync(music, new Progress<Echo.Contracts.Scanning.ScanProgress>(), token);
    remote = await CoreConnection.ConnectRemoteAsync(profile, token);
    await remote.StartAsync(token);
    Check(remote.IsRemote && (await remote.InfoAsync(token)).Instance == (await local.InfoAsync(token)).Instance, "Remote created another owner");
    Check((await remote.Library.GetTracksAsync(new(), token)).Items.Count == 1, "Remote browsing failed");
    var session = remote.Playback.LocalZone.Session;
    var tracks = await session.ResolveFilesAsync([song], token);
    var injected = tracks[0] with { Source = new("http", "http://127.0.0.1/never-fetch", Headers: new Dictionary<string, string> { ["Authorization"] = "not-a-real-secret" }) };
    await session.SetQueueAsync([injected], injected.ItemId, token);
    await Until(() => session.Snapshot.Status == PlaybackStatus.Playing, token);
    await session.PauseAsync(token);
    await Until(() => local.Playback.LocalZone.Session.Snapshot.Status == PlaybackStatus.Paused, token);
    Check(session.Queue[0].Source is null && session.Queue[0].FilePath == song, "Remote source projection was not sanitized");
    Check(Stopwatch.GetElapsedTime(session.State.ClockTimestamp).Duration() < TimeSpan.FromSeconds(2), "Remote clock was not anchored locally");
    var accepted = local.Playback.LocalZone.Session.Queue[0];
    Check(accepted.Source?.Kind != "http", "Remote URI override reached the native owner");
    Console.WriteLine("REMOTE_STAGE permissions");
    await Forbidden(() => remote.RemoteSources.LoadAsync(token));
    await Forbidden(() => remote.Library.ImportAsync(music, new Progress<Echo.Contracts.Scanning.ScanProgress>(), token));
    await Forbidden(() => remote.ShutdownAsync(token));
    await Forbidden(() => session.ResolveFilesAsync([Path.Combine(root, "not-in-library.wav")], token));
    await Forbidden(() => remote.Lyrics.ReadAsync(song, Path.Combine(root, "private.txt"), token));
    try { await remote.Backup.PrepareRestoreAsync(token); throw new Exception("Remote restore crossed into local filesystem"); } catch (NotSupportedException) { }
    Check((await local.InfoAsync(token)).Instance == remote.Welcome.Instance, "Forbidden command stopped the owner");
    await remote.DisposeAsync(); remote = null;
    Console.WriteLine("REMOTE_STAGE trust");
    try { await using var invalid = await CoreConnection.ConnectRemoteAsync(profile with { CertificateSha256 = new string('0', 64) }, token); throw new Exception("Wrong certificate accepted"); }
    catch (AuthenticationException) { }
    try { await using var invalid = await CoreConnection.ConnectRemoteAsync(profile with { AccessToken = new string('0', 64) }, token); throw new Exception("Wrong pairing accepted"); }
    catch (CoreRemoteException) { }
    remote = await CoreConnection.ConnectRemoteAsync(profile, token);
    Check(remote.Playback.LocalZone.Session.Snapshot.Status == PlaybackStatus.Paused, "Detach changed playback");
    Console.WriteLine("REMOTE_STAGE revoke");
    RemoteCoreAccess.Revoke(endpoint, profile.ClientId);
    await Until(() => !remote.Playback.LocalZone.Session.State.IsAvailable, token);
    Check((await local.InfoAsync(token)).Instance == remote.Welcome.Instance, "Revocation damaged local control");
    try { await using var invalid = await CoreConnection.ConnectRemoteAsync(profile, token); throw new Exception("Revoked pairing accepted"); }
    catch (CoreRemoteException) { }
    await local.ShutdownAsync(token); await process.WaitForExitAsync(token);
    Console.WriteLine("PASS TLS pairing, shared owner, remote browsing/playback, trusted source reconstruction, sanitized state, denied admin/file/credential access, wrong pin/token rejection, bounded revocation and local owner survival");
}
finally
{
    if (remote is not null) await remote.DisposeAsync();
    if (local is not null) await local.DisposeAsync();
    if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
}
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static async Task Forbidden(Func<Task> action)
{
    try { await action(); throw new Exception("Remote acquired a forbidden capability"); }
    catch (CoreRemoteException error) when (error.Code == "forbidden") { }
}
static async Task Until(Func<bool> condition, CancellationToken token) { while (!condition()) await Task.Delay(30, token); }
static void Wave(string path)
{
    using var writer = new BinaryWriter(File.Create(path)); const int size = 64;
    writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8); writer.Write(16);
    writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000);
    writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(size); writer.Write(new byte[size]);
}

// Pairing artifacts are disposable even when enrollment fails before a process starts.
sealed class TestDirectory(string path) : IDisposable
{
    public void Dispose() { if (Directory.Exists(path)) Directory.Delete(path, true); }
}
