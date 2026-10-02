using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Echo.Contracts.Library;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Settings.Runtime;
using Echo.Desktop.Shell;
using Echo.Playback;

if (args.Length > 0 && args[0] == "--native")
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native taskbar smoke requires Windows.");
    Exception? failure = null;
    var thread = new Thread(() => { try { NativeWindowSmoke.Run(Path.GetFullPath(args.Length > 1 ? args[1] : "misc/taskbar")); } catch (Exception error) { failure = error; } });
    thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    if (failure is not null) throw failure;
    return;
}
AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var visualOnly = args.Length > 0 && args[0] == "--visual";
var output = Path.GetFullPath(visualOnly ? args.Length > 1 ? args[1] : "misc/taskbar" : args.Length > 0 ? args[0] : "misc/taskbar");
Directory.CreateDirectory(output);
TaskbarVisualCapture.Save(output);
if (visualOnly) return;
var audio = new AudioFixture();
var library = new LibraryFixture();
var session = new PlaybackSessionCoordinator(audio);
var shell = new ShellViewModel();
shell.Text.SetLanguage("zh-CN");
var player = new PlayerViewModel(new PlaybackZone("local", "本机", session), shell.Text);
shell.AttachPlayback(player); player.Details.AttachLibrary(library);
var window = new MainWindow { DataContext = shell };
using var settings = new SettingsFeatureRuntime(shell.FeaturePreferences);
using var binding = new WindowTaskbarBinding(window, shell, settings, new(window));
shell.AttachFeatureSettings(settings);
try
{
    window.Show(); Pump();
    var file = Path.Combine(output, "fixture.wav"); File.WriteAllBytes(file, [0]);
    var a = new QueueTrack("a", "a", file, Metadata: new Dictionary<string, string?> { ["title"] = "老人と海", ["artist"] = "ヨルシカ" });
    var b = a with { ItemId = "b", TrackId = "b" };
    Wait(session.SetQueueAsync([a, b], "a")); Started();
    Until(() => player.Details.CanFavorite);
    Wait(binding.ExecuteCommandAsync(2)); Check(session.Snapshot.Status == PlaybackStatus.Paused, "pause");
    Wait(binding.ExecuteCommandAsync(2)); Started(); Check(session.Snapshot.Status == PlaybackStatus.Playing, "resume");
    Wait(binding.ExecuteCommandAsync(3)); Started(); Check(session.Snapshot.Current?.ItemId == "b", "next");
    Wait(binding.ExecuteCommandAsync(1)); Started(); Check(session.Snapshot.Current?.ItemId == "a", "previous");
    Until(() => player.Details.CanFavorite);
    Wait(binding.ExecuteCommandAsync(4)); Check(library.Favorite && player.Details.IsFavorite, "confirmed favorite");
    Wait(binding.ExecuteCommandAsync(4)); Check(!library.Favorite && !player.Details.IsFavorite, "unfavorite");
    audio.Notify("audio.error", $"{{\"operationId\":{session.Snapshot.OperationId},\"message\":\"fixture recovery\"}}");
    Until(() => session.Snapshot.Status == PlaybackStatus.Faulted);
    Wait(binding.ExecuteCommandAsync(2)); Started(); Check(session.Snapshot.Status == PlaybackStatus.Playing, "fault recovery");
    shell.UltraLightEnabled = true; window.WindowState = Avalonia.Controls.WindowState.Minimized; Pump();
    Check(!window.IsSurfaceResident, "parked surface");
    Wait(binding.ExecuteCommandAsync(2)); Check(session.Snapshot.Status == PlaybackStatus.Paused && !window.IsSurfaceResident, "background pause without restoring visuals");
    Wait(binding.ExecuteCommandAsync(5)); Pump();
    Check(window.IsSurfaceResident && shell.IsQueueDrawerOpen, "queue restores parked window");
    shell.FeaturePreferences.Confirm("taskbar-mini-player", new(false));
    Wait(binding.ExecuteCommandAsync(2)); Check(session.Snapshot.Status == PlaybackStatus.Paused, "disabled click ignored");
    player.Details.AttachLibrary(null); Check(!player.Details.CanFavorite, "external files cannot favorite");
    Wait(binding.ExecuteCommandAsync(4)); Check(library.Writes == 2, "unavailable favorite does not write");
    shell.Text.SetLanguage("en-US"); Check(shell.Text["taskbar.coverAndInfo"] == "Artwork and track information", "English options");
    NativeResourcesSmoke.Run(output);
    Console.WriteLine("PASS: five commands, confirmed favorites, parked playback/queue recovery, disabled controls, unavailable favorite, localization, raster capture, native resources. Audio/library are fixtures. Explorer hover/physical input not verified.");
}
finally
{
    binding.Dispose(); window.RequestExit(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization();
    Wait(session.DisposeAsync().AsTask());
}
void Started() { audio.Notify("audio.started", $"{{\"operationId\":{session.Snapshot.OperationId}}}"); Until(() => session.Snapshot.Status == PlaybackStatus.Playing); }
static void Pump() => Dispatcher.UIThread.RunJobs();
static void Until(Func<bool> done)
{
    var timer = Stopwatch.StartNew();
    while (!done()) { Pump(); Thread.Sleep(3); if (timer.Elapsed.TotalSeconds > 3) throw new TimeoutException("Taskbar condition"); }
    Pump();
}
static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class LibraryFixture : IPlayerTrackLibrary
{
    public bool Favorite; public int Writes;
    public Task<TrackRow?> FindTrackByPathAsync(string path, CancellationToken cancellation)
        => Task.FromResult<TrackRow?>(new("a", path, "老人と海", "ヨルシカ", "老人と海", 120, "flac", 44100, 16, "a", Favorite: Favorite));
    public Task SetTrackFavoriteAsync(string trackId, bool favorite, CancellationToken cancellation)
    { cancellation.ThrowIfCancellationRequested(); Favorite = favorite; Writes++; return Task.CompletedTask; }
}
sealed class AudioFixture : INativeAudio
{
    public event Action<string, JsonElement>? Notification;
    public event Action<Exception>? Failed { add { } remove { } }
    private int operation;
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        var json = method switch
        {
            "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
            "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":44100,\"channels\":2}}",
            "audio.openFile" => $"{{\"operationId\":{++operation},\"durationSeconds\":120,\"sampleRate\":44100}}",
            _ => "true"
        };
        using var document = JsonDocument.Parse(json); return Task.FromResult(document.RootElement.Clone());
    }
    public void Notify(string method, string json) { using var data = JsonDocument.Parse(json); Notification?.Invoke(method, data.RootElement.Clone()); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
