using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Shell;
using Echo.Playback;

internal static class PlaybackCloseSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var audio = new AudioFixture();
        var session = new PlaybackSessionCoordinator(audio);
        var shell = new ShellViewModel();
        shell.Text.SetLanguage("zh-CN");
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session), shell.Text);
        shell.AttachPlayback(player);
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show(); Pump();
            var path = Path.Combine(output, "fixture.wav");
            File.WriteAllBytes(path, [0]);
            Wait(session.PlayAsync(new QueueTrack("close", "close", path)));
            audio.Notify("audio.started", "{\"operationId\":1}");
            Until(() => session.Snapshot.Status == PlaybackStatus.Playing);
            window.Close(); Pump();
            var dialog = window.OwnedWindows.OfType<PlaybackCloseDialog>().Single();
            Require(window.IsVisible && !window.IsExitRequested && audio.Stops == 0,
                "close keeps a visible owner and never silently stops the Core session");
            Save(dialog, Path.Combine(output, "playback-close-light.png"));
            dialog.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            Save(dialog, Path.Combine(output, "playback-close-dark.png"));
            window.Close(); Pump();
            Require(window.OwnedWindows.OfType<PlaybackCloseDialog>().Count() == 1, "repeated close reuses the choice");
            dialog.Close(PlaybackCloseChoice.Cancel); Pump();
            Require(window.IsVisible && !window.IsExitRequested && audio.Stops == 0, "cancel leaves playback and window intact");
            window.Close(); Pump();
            dialog = window.OwnedWindows.OfType<PlaybackCloseDialog>().Single();
            dialog.FindControl<Button>("BackgroundButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Require(window.IsVisible && window.ShowInTaskbar && window.WindowState == WindowState.Minimized && audio.Stops == 0,
                "background choice without native tray keeps the taskbar recovery entry and playback");
            window.RestoreMainWindow(); Pump();
            shell.CloseToTray = true;
            window.Close(); Pump();
            Require(window.WindowState == WindowState.Minimized && !window.IsExitRequested && audio.Stops == 0 &&
                    session.Snapshot.Status == PlaybackStatus.Playing,
                "saved background policy keeps playback running");
            window.RestoreMainWindow(); Pump();
            shell.CloseToTray = false;
            audio.FailStop = true;
            window.Close();
            Until(() => shell.Notice.Length > 0);
            Require(window.IsVisible && !window.IsExitRequested && shell.Notice.Length > 0,
                "failed native stop keeps the controller available with an error");
            audio.FailStop = false;
            audio.StopReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Close(); Pump();
            window.Close(); Pump();
            Require(audio.Stops == 2 && !window.OwnedWindows.OfType<PlaybackCloseDialog>().Any(),
                "saved direct exit sends one stop request without opening another choice");
            Require(!window.IsExitRequested && window.IsVisible, "controller stays available until native stop acknowledgement");
            using (var accepted = JsonDocument.Parse("true")) audio.StopReply.SetResult(accepted.RootElement.Clone());
            Until(() => window.IsExitRequested && !window.IsVisible);
            Require(audio.Stops == 2 && session.Snapshot.Status == PlaybackStatus.Stopped,
                "explicit stop closes only after the native stop command succeeds");
            Console.WriteLine("PASS saved direct exit waits for stop acknowledgement, repeated close sends one stop, failed stop keeps the window, and saved background policy preserves playback. Legacy close choice also passed. Native tray input and physical audio are not covered.");
        }
        finally
        {
            window.RequestExit(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization();
            Wait(session.DisposeAsync().AsTask());
        }
    }

    private static void Save(Window window, string path)
    {
        Pump();
        using var image = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1), new Vector(96, 96));
        image.Render(window); image.Save(path, PngBitmapEncoderOptions.Default);
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Until(Func<bool> done)
    {
        var time = Stopwatch.StartNew();
        while (!done()) { Pump(); Thread.Sleep(5); if (time.Elapsed.TotalSeconds > 5) throw new TimeoutException("Playback close UI condition"); }
        Pump();
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }

    private sealed class AudioFixture : INativeAudio
    {
        public event Action<string, JsonElement>? Notification;
        public event Action<Exception>? Failed { add { } remove { } }
        public bool FailStop;
        public int Stops;
        public TaskCompletionSource<JsonElement>? StopReply;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            if (method == "audio.stop")
            {
                Stops++;
                if (FailStop)
                {
                    using var rejected = JsonDocument.Parse("false");
                    return Task.FromResult(rejected.RootElement.Clone());
                }
                if (StopReply is not null) return StopReply.Task;
            }
            var json = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":44100,\"channels\":2}}",
                "audio.openFile" => "{\"operationId\":1,\"durationSeconds\":120,\"sampleRate\":44100}",
                _ => "true"
            };
            using var data = JsonDocument.Parse(json); return Task.FromResult(data.RootElement.Clone());
        }
        public void Notify(string method, string json)
        { using var data = JsonDocument.Parse(json); Notification?.Invoke(method, data.RootElement.Clone()); }
        public ValueTask DisposeAsync() { Notification = null; return ValueTask.CompletedTask; }
    }
}
