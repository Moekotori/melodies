using System.Diagnostics;
using Echo.Playback;
using Echo.Playback.NativeAudio;

var binary = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("native/runtime/win-x64/echo-audio-host.exe");
var fixture = Path.GetFullPath("misc/playback-smoke/a.wav");
if (!File.Exists(fixture)) throw new Exception("先运行 Echo.Playback.Smoke 生成无声 fixture");
var timer = Stopwatch.StartNew();
var host = new NativeAudioHost(binary);
var firstPcm = 0;
long maxFrames = 0;
host.Notification += (method, payload) =>
{
    if (method == "audio.firstPcm") Interlocked.Increment(ref firstPcm);
    if (method == "audio.position" && payload.TryGetProperty("framesPlayed", out var frames)) Interlocked.Exchange(ref maxFrames, frames.GetInt64());
};
await using var session = new PlaybackSessionCoordinator(host);
var modes = await session.GetOutputModesAsync();
Console.WriteLine("Compiled modes: " + string.Join(", ", modes.Select(x => x.Key)));
IReadOnlyList<AudioOutputDevice> asio = [];
foreach (var mode in modes)
{
    var devices = await session.GetOutputDevicesAsync(mode.Key);
    Console.WriteLine($"{mode.Key}: {devices.Count} native devices");
    if (mode.Key == "asio") asio = devices;
}
await host.SetOutputChannelsAsync(8);
await host.StartAsync();
Check(host.OutputChannels == 8 && host.Capabilities.HasValue, "eight-channel deferred bootstrap");
await host.SetOutputChannelsAsync(2);
await host.StartAsync();
await session.ConfigureOutputAsync(new AudioOutputConfiguration());
await session.SetVolumeAsync(0);
await session.PlayAsync(new("output-smoke", "output-smoke", fixture));
await UntilAsync(() => session.Snapshot.Status == PlaybackStatus.Playing, 2500);
var missing = new AudioOutputDevice("asio:ECHO missing smoke driver", "ECHO missing smoke driver", "asio", 999, false, 0, []);
try
{
    await session.ConfigureOutputAsync(new AudioOutputConfiguration("asio", missing, Channels: 8));
    throw new Exception("missing ASIO driver unexpectedly accepted playback");
}
catch (IOException e) when (e.Message.Contains("已恢复原输出配置", StringComparison.Ordinal)) { }
Check(host.OutputChannels == 2 && session.Snapshot.Status is PlaybackStatus.Playing or PlaybackStatus.Loading, "ASIO failure must restore shared stereo playback");
await session.StopAsync();
Console.WriteLine("PASS eight-channel bootstrap/rebuild, rejected ASIO restores native shared playback");

// A muted, short hardware attempt. Unsupported/disconnected drivers are reported, never passed.
var realtek = asio.FirstOrDefault(x => x.Name.Contains("Realtek", StringComparison.OrdinalIgnoreCase));
if (realtek is not null && !args.Contains("--skip-hardware"))
{
    try
    {
        firstPcm = 0; maxFrames = 0;
        await session.ConfigureOutputAsync(new AudioOutputConfiguration("asio", realtek, SampleRate: 48000, BufferFrames: 512));
        await session.ResumeAsync();
        await UntilAsync(() => session.Snapshot.Status == PlaybackStatus.Playing, 2000);
        var output = session.Snapshot.Output!.Value;
        Check(output.GetProperty("backend").GetString() == "asio", "actual ASIO backend");
        Check(output.GetProperty("channels").GetInt32() == 2, "actual ASIO stereo");
        Console.WriteLine($"ASIO hardware PASS: {realtek.Name}, {output.GetProperty("sampleRate")}, {output.GetProperty("actualBufferFrames")} frames");
    }
    catch (Exception e)
    {
        Console.WriteLine($"ASIO hardware NOT VERIFIED: {e.Message}; status={session.Snapshot.Status}, firstPCM={firstPcm}, sourceFrames={maxFrames}");
        Console.WriteLine("ASIO ready facts: " + session.Snapshot.Output?.GetRawText());
    }
    finally
    {
        await session.StopAsync();
        await session.ConfigureOutputAsync(new AudioOutputConfiguration());
    }
}
Console.WriteLine($"Output smoke completed ({timer.ElapsedMilliseconds} ms)");
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task UntilAsync(Func<bool> condition, int timeout)
{
    var clock = Stopwatch.StartNew();
    while (!condition()) { if (clock.ElapsedMilliseconds > timeout) throw new TimeoutException("native output confirmation"); await Task.Delay(10); }
}
