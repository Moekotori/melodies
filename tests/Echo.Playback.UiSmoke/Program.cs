using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Echo.Desktop;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Playback;
using Echo.Playback;
using Echo.Playback.NativeAudio;
using Echo.Application.Playback;
using Echo.Desktop.Bootstrap;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = true }).SetupWithoutStarting();
var root = Path.GetFullPath("misc/playback-ui-smoke");
Directory.CreateDirectory(root);
using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var native = new NativeAudioHost(Path.GetFullPath("native/runtime/win-x64/echo-audio-host.exe"));
var runtime = new PlaybackRuntime(native);
try
{
Pump(runtime.StartAsync());
var session = runtime.LocalZone.Session;
var player = new PlayerViewModel(runtime.LocalZone);
var shell = new ShellViewModel(); shell.AttachPlayback(player);
shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == Echo.Desktop.Motion.MotionMode.Off);
var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
window.Show();
Dispatcher.UIThread.RunJobs();
AvaloniaHeadlessPlatform.ForceRenderTimerTick();
using (var empty = window.CaptureRenderedFrame() ?? throw new Exception("PlayerBar failed to render")) empty.Save(Path.Combine(root, "player-empty.png"), PngBitmapEncoderOptions.Default);
var paths = new[] { Path.GetFullPath("misc/playback-smoke/a.wav"), Path.GetFullPath("misc/playback-smoke/b.wav") };
var action = Task.Run(async () => { await session.SetVolumeAsync(0); await session.SetQueueAsync(paths.Select((path, i) => new QueueTrack(i.ToString(), i.ToString(), path)), "0"); });
Pump(action);
Console.WriteLine("UI native queue opened");
var wait = Task.Run(async () =>
{
    while (session.Snapshot.Status != PlaybackStatus.Playing) await Task.Delay(10, limit.Token);
    await session.PauseAsync();
});
Pump(wait);
Console.WriteLine("UI native pause confirmed");
Dispatcher.UIThread.RunJobs();
if (player.Title == "暂无播放" || !player.HasTrack || player.PlayIcon != "play") throw new Exception("PlayerBar bindings did not update");
var second = new PlayerViewModel(runtime.LocalZone);
Dispatcher.UIThread.RunJobs();
if (!second.HasTrack || second.Title != player.Title || second.Position != player.Position)
    throw new Exception("A newly connected control did not receive the current zone snapshot");
player.Detach();
var detachedPosition = player.Position;
Pump(second.SeekToAsync(0.5));
if (Math.Abs(second.Position - 0.5) > 0.01 || player.Position != detachedPosition || native.ProcessId is null)
    throw new Exception("Disconnecting one control stopped playback or delivered late UI state");
second.Detach();
player = new PlayerViewModel(runtime.LocalZone);
shell.AttachPlayback(player);
Dispatcher.UIThread.RunJobs();
if (!player.HasTrack || Math.Abs(player.Position - 0.5) > 0.01 || player.Zone.Id != PlaybackZone.LocalId)
    throw new Exception("Reconnecting a control lost the zone queue or playback position");
AvaloniaHeadlessPlatform.ForceRenderTimerTick();
using (var frame = window.CaptureRenderedFrame() ?? throw new Exception("PlayerBar failed to render")) frame.Save(Path.Combine(root, "player-paused.png"), PngBitmapEncoderOptions.Default);
var zoneButton = window.GetVisualDescendants().OfType<Button>().First(x => x.Name == "OutputSettingsButton");
zoneButton.Flyout!.ShowAt(zoneButton);
Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
using (var zoneFrame = window.CaptureRenderedFrame() ?? throw new Exception("Zone output panel failed to render"))
    zoneFrame.Save(Path.Combine(root, "zone-output.png"), PngBitmapEncoderOptions.Default);
zoneButton.Flyout.Hide();
Pump(player.AudioSettings.RefreshCommand.ExecuteAsync(null));
var asioMode = player.AudioSettings.Modes.FirstOrDefault(x => x.Key == "asio");
if (asioMode is not null)
{
    player.AudioSettings.SelectedMode = asioMode;
    Pump(Task.Run(async () => { while (player.AudioSettings.IsRefreshing) await Task.Delay(10, limit.Token); }));
    if (player.AudioSettings.SelectedDevice?.Mode != "asio") throw new Exception("ASIO device selection bindings");
    var settingsButton = window.GetVisualDescendants().OfType<Button>().First(x => x.Name == "OutputSettingsButton");
    settingsButton.Flyout!.ShowAt(settingsButton);
    Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var settings = window.CaptureRenderedFrame() ?? throw new Exception("ASIO settings failed to render")) settings.Save(Path.Combine(root, "asio-settings.png"), PngBitmapEncoderOptions.Default);
    settingsButton.Flyout.Hide();
}
var checkpoint = session.ExportCheckpointAsync(); Pump(checkpoint);
var storage = Task.Run(async () =>
{
    await using var db = await PlaybackStateDatabase.OpenAsync(Path.Combine(root, "state.sqlite"), limit.Token);
    await db.SaveAsync(checkpoint.GetAwaiter().GetResult(), limit.Token);
    if (await db.ReadAsync(limit.Token) != checkpoint.GetAwaiter().GetResult()) throw new Exception("checkpoint SQLite roundtrip");
});
Pump(storage);
player.Detach(); window.Close();
if (native.ProcessId is null) throw new Exception("Closing a view disposed the application-owned audio host");
var sharedWindow = DesktopComposition.CreateWindow(false, false, playback: runtime);
sharedWindow.Show(); Dispatcher.UIThread.RunJobs();
sharedWindow.Close();
while (sharedWindow.IsVisible) { limit.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); }
if (native.ProcessId is null || runtime.LocalZone.Session.Queue.Count != 2)
    throw new Exception("Desktop composition released an injected core or lost its queue");
Pump(runtime.DisposeAsync().AsTask());
Console.WriteLine("PASS actual PlayerBar bindings, two controls, detach/reconnect, window-independent native lifetime, SQLite checkpoint, headless render");
}
finally
{
    var release = runtime.DisposeAsync().AsTask();
    var deadline = System.Diagnostics.Stopwatch.StartNew();
    while (!release.IsCompleted && deadline.ElapsedMilliseconds < 5000)
    { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); }
    if (!release.IsCompleted) throw new TimeoutException("Runtime cleanup exceeded five seconds");
    release.GetAwaiter().GetResult();
}

void Pump(Task task)
{
    while (!task.IsCompleted) { limit.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); }
    task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
}
