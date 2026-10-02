using Echo.Playback;
using Echo.Playback.NativeAudio;
using Echo.Application.Playback;

if (args.Length < 2)
{
    Console.WriteLine("Echo.Player <native-host-path> <audio-file> [audio-file ...]\n命令: pause, play, next, prev, seek <秒>, volume <0..1>, repeat off|one|all, devices, zone, recover, quit");
    return;
}
await using var runtime = new PlaybackRuntime(new NativeAudioHost(args[0]));
await runtime.StartAsync();
var player = runtime.LocalZone.Session;
var items = args.Skip(1).Select((path, i) => new QueueTrack(i.ToString(), i.ToString(), Path.GetFullPath(path))).ToArray();
PlaybackStatus? previous = null;
string? previousItem = null;
player.Changed += state =>
{
    if (state.Status != previous || state.Current?.ItemId != previousItem)
    {
        Console.WriteLine($"{state.Status} · item {state.Current?.ItemId} · {state.SampleRate} Hz / {state.Channels} ch" + (state.Error is null ? "" : " · 播放失败"));
        previous = state.Status; previousItem = state.Current?.ItemId;
    }
};
try
{
    await player.SetQueueAsync(items, items[0].ItemId);
    while (await Console.In.ReadLineAsync() is { } line && line != "quit")
    {
        var command = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        try
        {
            switch (command.FirstOrDefault())
            {
                case "pause": await player.PauseAsync(); break;
                case "play": await player.ResumeAsync(); break;
                case "next": await player.NextAsync(); break;
                case "prev": await player.PreviousAsync(); break;
                case "seek" when command.Length == 2: await player.SeekAsync(double.Parse(command[1], System.Globalization.CultureInfo.InvariantCulture)); break;
                case "volume" when command.Length == 2: await player.SetVolumeAsync(double.Parse(command[1], System.Globalization.CultureInfo.InvariantCulture)); break;
                case "repeat" when command.Length == 2: await player.SetRepeatAsync(Enum.Parse<RepeatMode>(command[1], true)); break;
                case "devices": Console.WriteLine((await player.GetDevicesAsync()).GetRawText()); break;
                case "zone": Console.WriteLine($"{runtime.LocalZone.Id} · {runtime.LocalZone.Name} · {runtime.LocalZone.Output.DeviceName}"); break;
                case "recover": await player.RecoverAsync(); break;
                default: Console.WriteLine("无法识别的命令"); break;
            }
        }
        catch (Exception) { Console.WriteLine("操作失败，请检查输出设备或使用 recover"); }
    }
}
catch (Exception e) { Console.Error.WriteLine($"播放器启动失败: {e.GetType().Name}"); Environment.ExitCode = 1; }
