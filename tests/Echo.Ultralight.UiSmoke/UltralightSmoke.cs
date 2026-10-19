using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Preferences;
using Echo.Playback;

internal static class UltralightSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var audio = new AudioFixture();
        var session = new PlaybackSessionCoordinator(audio);
        var shell = new ShellViewModel();
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session), shell.Text);
        shell.AttachPlayback(player); shell.Text.SetLanguage("zh-CN");
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show(); Pump();
            shell.Settings.SelectCategoryCommand.Execute("general");
            shell.Settings.SelectTabCommand.Execute("窗口与启动"); Pump();
            TrayOwnershipSmoke.Run(window, shell);
            var row = shell.Settings.Sections.SelectMany(section => section.Rows).Single(x => x.Id == "ultra-light-mode");
            Require(row.IsEnabled && !row.BoolValue, "default-off setting is usable");
            window.WindowState = WindowState.Minimized; Pump();
            Require(window.IsSurfaceResident, "ordinary minimize keeps its interface");
            window.RestoreMainWindow();
            var path = Path.Combine(output, "fixture.wav"); File.WriteAllBytes(path, [0]);
            var tracks = new[] { new QueueTrack("first", "first", path), new QueueTrack("second", "second", path) };
            Wait(session.SetQueueAsync(tracks, "first"));
            audio.Notify("audio.started", "{\"operationId\":1}");
            Until(() => session.Snapshot.Status == PlaybackStatus.Playing);
            shell.NavigateCommand.Execute("settings"); Pump();
            row = shell.Settings.Sections.SelectMany(section => section.Rows).Single(x => x.Id == "ultra-light-mode");
            row.BoolValue = true; Pump();
            var calls = audio.Calls; var operation = session.Snapshot.OperationId; var generation = session.State.Generation;
            var retired = Minimize(window); Pump();
            Require(!window.IsSurfaceResident && window.Content is null && NameScope.GetNameScope(window) is null,
                "minimize releases both the visuals and namescope");
            Require(window.ShowInTaskbar && shell.Player.Queue.Rows.Count == 0, "native taskbar entry survives; row projections retire");
            Require(audio.Calls == calls && audio.Starts == 1 && session.Snapshot.OperationId == operation,
                "entry does not send audio commands or restart native audio");
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); Pump(); }
            Require(!retired.IsAlive, "retired visual tree is collectible");
            audio.Notify("audio.ended", $"{{\"queueAdvance\":true,\"fromOperationId\":1,\"queueRevision\":{audio.QueueRevision},\"nextItemId\":\"second\",\"operationId\":2}}");
            Until(() => session.Snapshot.Current?.ItemId == "second");
            Require(!window.IsSurfaceResident && session.State.Generation == generation, "EOF advances through the same core while unloaded");
            Wait(window.ExecuteShortcutAsync("playPause"));
            Require(session.Snapshot.Status == PlaybackStatus.Paused && !window.IsSurfaceResident, "background playback controls do not rebuild the UI");
            Wait(window.ExecuteShortcutAsync("showMainWindow")); Pump();
            Require(window.IsSurfaceResident && shell.ActiveRouteId == "settings", "restore rebuilds the selected page");
            shell.ToggleQueueDrawerCommand.Execute(null); Pump();
            Until(() => shell.Player.Queue.Rows.Any(item => item.ItemId == "second"));
            shell.CloseQueueDrawerCommand.Execute(null); Pump();
            Require(audio.Starts == 1 && session.Snapshot.OperationId == 2, "restore does not reopen audio");
            Save(window, output, "ultralight-settings-light.png");
            shell.IsDark = true; Pump(); Save(window, output, "ultralight-settings-dark.png");
            var hiddenTree = Hide(window); Pump(); Require(!window.IsSurfaceResident, "explicit hiding also unloads");
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); Pump(); }
            Require(!hiddenTree.IsAlive, "hidden visual tree is collectible even after active focus was cleared");
            shell.Floating.MiniEnabled = true; Pump();
            var oldPosition = player.Position;
            audio.Notify("audio.position", "{\"operationId\":2,\"framesPlayed\":88200}");
            Until(() => player.Position != oldPosition);
            Require(!window.IsSurfaceResident, "floating consumers retain clock updates without main visuals");
            shell.Floating.MiniEnabled = false;
            shell.UltraLightEnabled = false; Pump();
            Require(window.IsSurfaceResident && !window.IsVisible, "turning off rebuilds without showing a hidden window");
            window.RestoreMainWindow();
            for (var i = 0; i < 3; i++)
            {
                shell.UltraLightEnabled = true; window.WindowState = WindowState.Minimized; Pump();
                window.RestoreMainWindow(); Pump();
                Require(window.IsSurfaceResident && session.Snapshot.OperationId == 2, "rapid parking/restore retains the active session");
            }
            var store = new JsonDesktopPreferencesStore(Path.Combine(output, "preferences.json"));
            store.Save(new DesktopPreferences(Features: shell.FeaturePreferences.Snapshot())); Wait(store.DisposeAsync().AsTask());
            var reader = new JsonDesktopPreferencesStore(Path.Combine(output, "preferences.json"));
            var read = reader.ReadAsync(CancellationToken.None); Wait(read);
            Require(read.Result?.Features?["ultra-light-mode"].Enabled == true, "switch survives the real JSON preference store");
            Wait(reader.DisposeAsync().AsTask());
            shell.CloseToTray = true;
            Require(!window.TryHideToTray() && window.ShowInTaskbar, "unsupported headless tray cannot hide the recovery entry");
            var exitIcon = window.SetPlatformTrayMenu(new NativeMenu());
            window.WindowState = WindowState.Minimized; Pump(); window.RequestExit(); Pump();
            Require(window.IsExitRequested && !window.IsVisible, "explicit exit closes an unloaded window");
            Require(!TrayIcon.GetIcons(Application.Current!)!.Contains(exitIcon!), "exit releases the shared platform icon");
            Console.WriteLine("Ultralight: settings tab, shared tray ownership, minimize/hide, collectible trees, unchanged audio owner, EOF, background controls, restore, floating clock, JSON, rapid reversal and exit passed. Audio is a protocol fixture; physical tray/taskbar input is not verified here.");
        }
        finally { window.RequestExit(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization(); Wait(session.DisposeAsync().AsTask()); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Minimize(MainWindow window)
    {
        window.FindControl<MotionPresenter>("PageHost")!.Focus();
        var weak = new WeakReference(window.Content); window.WindowState = WindowState.Minimized; return weak;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Hide(MainWindow window)
    {
        window.FindControl<MotionPresenter>("PageHost")!.Focus();
        window.FocusManager!.Focus(null);
        var weak = new WeakReference(window.Content); window.Hide(); return weak;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Until(Func<bool> done)
    {
        var time = Stopwatch.StartNew();
        while (!done()) { Pump(); Thread.Sleep(5); if (time.Elapsed.TotalSeconds > 3) throw new TimeoutException("Ultralight UI condition"); }
        Pump();
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Save(MainWindow window, string output, string name)
    {
        Pump(); using var bitmap = new RenderTargetBitmap(new PixelSize(1440, 900), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
    }
    private sealed class AudioFixture : INativeAudio
    {
        public event Action<string, JsonElement>? Notification;
        public event Action<Exception>? Failed { add { } remove { } }
        public int Calls, Starts;
        public long QueueRevision;
        public Task StartAsync(CancellationToken cancellationToken = default) { Starts++; return Task.CompletedTask; }
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var json = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{QueueRevision = parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":44100,\"channels\":2}}",
                "audio.openFile" => "{\"operationId\":1,\"durationSeconds\":120,\"sampleRate\":44100}",
                _ => "true"
            };
            using var data = JsonDocument.Parse(json); return Task.FromResult(data.RootElement.Clone());
        }
        public void Notify(string method, string json) { using var data = JsonDocument.Parse(json); Notification?.Invoke(method, data.RootElement.Clone()); }
        public ValueTask DisposeAsync() { Notification = null; return ValueTask.CompletedTask; }
    }
}
