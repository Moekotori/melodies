using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Dsp;
using Echo.Playback;
using Echo.Playback.NativeAudio;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var root = Path.GetFullPath(Path.Combine("misc/headphone-smoke", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
using var service = new DspResourceService(root);
HeadphoneCatalogChecks.Run(service, root);
Pump(HeadphoneReceiptChecks.RunAsync());
var host = new NativeAudioHost(Path.GetFullPath("native/runtime/win-x64/echo-audio-host.exe"), allowProDsp: true);
var session = new PlaybackSessionCoordinator(host);
try { HeadphoneUiChecks.Run(session, service, root); }
finally { Pump(session.DisposeAsync().AsTask()); }
Console.WriteLine("PASS headphone catalog, native receipts, profile recovery, UI consent and rendered light/dark/narrow views: " + root);

static void Pump(Task task)
{
    var deadline = Environment.TickCount64 + 20000;
    while (!task.IsCompleted && Environment.TickCount64 < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
    if (!task.IsCompleted) throw new TimeoutException("Headphone smoke timed out");
    task.GetAwaiter().GetResult();
}
