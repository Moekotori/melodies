using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Lyrics;
using Echo.Contracts.Settings;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Features.Floating;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Preferences;
using Echo.Playback;

internal static class FloatingWindowsSmoke
{
    public static void Run(string output, bool renderOnly = false)
    {
        Directory.CreateDirectory(output);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var native = new PreviewNative();
        var session = new PlaybackSessionCoordinator(native);
        var shell = new ShellViewModel();
        var player = new PlayerViewModel(new PlaybackZone("local", "Local", session), shell.Text);
        shell.AttachPlayback(player);
        var source = new PreviewLyrics(); shell.AttachLyrics(source, new LyricsAppearance(text: shell.Text));
        var window = new MainWindow { DataContext = shell, Width = 1200, Height = 800 };
        var lockCalls = 0; var locked = false;
        var controller = new FloatingWindowController(window, shell, (_, enabled) => { lockCalls++; locked = enabled; return true; });
        try
        {
            window.Show(); Settle();
            var first = new QueueTrack("a", "a", Path.Combine(output, "moonlight.flac"), Metadata: new Dictionary<string, string?> { ["title"] = "月光回声 · Moonlight Echo", ["artist"] = "ECHO Orchestra" });
            var second = new QueueTrack("b", "b", Path.Combine(output, "dawn.flac"), Metadata: new Dictionary<string, string?> { ["title"] = "下一站，黎明", ["artist"] = "ECHO Orchestra" });
            File.WriteAllBytes(first.FilePath, []); File.WriteAllBytes(second.FilePath, []);
            Pump(session.SetQueueAsync([first, second], "a", limit.Token)); Pump(session.SeekAsync(5, limit.Token));
            shell.Floating.MiniEnabled = true;
            if (renderOnly) { Wait(() => source.Reads > 0); Pump(Task.Delay(50, limit.Token)); }
            else Wait(() => controller.MiniWindow?.FindControl<TextBlock>("LyricLine")?.Text == "月光洒落在你我之间");
            var miniReads = source.Reads;
            Console.WriteLine("Floating smoke: mini lyrics loaded.");
            shell.Floating.LyricsEnabled = true; Settle();
            if (!renderOnly) Require(source.Reads == miniReads, "opening desktop lyrics reuses the mini player's loaded document");
            Console.WriteLine("Floating smoke: shared desktop document ready.");
            var mini = controller.MiniWindow!; var desktop = controller.LyricsWindow!;
            var canvas = desktop.FindControl<FloatingLyricsControl>("LyricsCanvas")!;
            if (renderOnly)
            {
                // Mirror the final XAML sizes when previewing a prior coherent build during parallel edits.
                foreach (var icon in mini.GetVisualDescendants().Concat(desktop.GetVisualDescendants()).OfType<Control>()
                    .Where(control => control is Echo.Desktop.Controls.EchoIcon or Echo.Desktop.Controls.GlyphIcon))
                { icon.Width = 18; icon.Height = 18; }
                var rail = window.GetVisualDescendants().OfType<PlayerBar>().First();
                var previewSeek = mini.FindControl<Slider>("SeekSlider")!;
                previewSeek.Theme = (Avalonia.Styling.ControlTheme)rail.FindResource("PlayerRail")!; previewSeek.Height = 24;
            }
            Capture(mini, "mini-compact-light.png"); Capture(desktop, "desktop-lyrics.png");
            if (renderOnly)
            {
                Click(mini, "ExpandButton"); Capture(mini, "mini-expanded-light.png");
                Click(mini, "QueueButton"); Capture(mini, "mini-queue-light.png");
                shell.IsDark = true; Settle(); Capture(mini, "mini-queue-dark.png");
                Console.WriteLine("Rendered native Avalonia floating windows from the available build. This is a fixture preview, not lifecycle acceptance.");
                return;
            }
            shell.Motion.ThemeAllowsAnimation = true;
            native.Position(2400); Wait(() => session.Snapshot.Status == PlaybackStatus.Playing);
            Console.WriteLine("Floating smoke: playback clock confirmed.");
            Pump(Task.Delay(45, limit.Token)); native.Position(4800); Wait(() => canvas.IsFrameDriverRunning);
            window.WindowState = WindowState.Minimized; Settle();
            Require(canvas.IsFrameDriverRunning, "visible desktop lyrics keep their own frame budget while main is minimized");
            shell.Floating.RestoreMainCommand.Execute(null);
            Console.WriteLine("Floating smoke: independent visible frame driver confirmed.");
            Capture(mini, "mini-compact-light.png"); Capture(desktop, "desktop-lyrics.png");
            Click(mini, "ExpandButton"); Require(mini.Width == 456 && mini.Height == 144, "expanded native bounds");
            Capture(mini, "mini-expanded-light.png");
            Click(mini, "PlayButton"); Wait(() => session.Snapshot.Status == PlaybackStatus.Paused);
            Require(!canvas.IsFrameDriverRunning, "paused driver stops");
            var seek = mini.FindControl<Slider>("SeekSlider")!;
            seek.Focus(); mini.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); mini.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Wait(() => session.Snapshot.PositionSeconds > 5); Require(mini.Player.Position == session.Snapshot.PositionSeconds, "mini seek shares confirmed clock");
            Click(mini, "QueueButton"); Require(mini.Height == 352, "queue finite viewport");
            Capture(mini, "mini-queue-light.png");
            shell.IsDark = true; Settle(); Capture(mini, "mini-queue-dark.png");
            Require(mini.RequestedThemeVariant == window.RequestedThemeVariant, "independent window follows theme");
            shell.Text.SetLanguage("en-US"); Settle(); Require((string)mini.FindResource("floating.restore")! == "Return to main window", "independent window language");
            shell.Floating.LyricsLocked = true; Require(locked && lockCalls > 0 && !desktop.CanResize, "lock adapter and resize");
            var stale = shell.Floating.Snapshot() with { LyricsBounds = new(int.MaxValue - 100, int.MaxValue - 100, 880, 190) };
            shell.Floating.Restore(stale); shell.Floating.ResetPositionCommand.Execute(null);
            Require(!locked && desktop.Position.X < 100000 && desktop.Position.Y < 100000, "reset rejects stale coordinates and unlocks");
            desktop.Hide(); Settle(); Require(!canvas.IsFrameDriverRunning, "hidden lyric window stops driver"); desktop.Show(); Settle();
            window.WindowState = WindowState.Minimized; shell.Floating.RestoreMainCommand.Execute(null); Require(window.WindowState == WindowState.Normal, "main restore");
            var saved = shell.Floating.Snapshot();
            var store = new JsonDesktopPreferencesStore(Path.Combine(output, "preferences.json"));
            store.Save(new DesktopPreferences(Floating: saved)); Pump(store.DisposeAsync().AsTask());
            var readStore = new JsonDesktopPreferencesStore(Path.Combine(output, "preferences.json"));
            var read = readStore.ReadAsync(limit.Token); Pump(read);
            Require(read.Result?.Floating == saved, "real JSON preferences roundtrip"); Pump(readStore.DisposeAsync().AsTask());
            shell.Floating.HideWithoutLyrics = true; source.HasLyrics = false;
            Pump(session.NextAsync(limit.Token)); Wait(() => !desktop.IsVisible);
            source.HasLyrics = true; Pump(session.PreviousAsync(limit.Token)); Pump(session.SeekAsync(5, limit.Token)); Wait(() => desktop.IsVisible && controller.MiniWindow!.FindControl<TextBlock>("LyricLine")!.Text == "月光洒落在你我之间");
            // Intro has no current row, but the track has lyrics: it must stay visible.
            Pump(session.SeekAsync(0, limit.Token)); Settle(); Require(desktop.IsVisible, "intro does not count as a lyric-less track");
            shell.Floating.MiniEnabled = false; shell.Floating.LyricsEnabled = false;
            Require(controller.MiniWindow is null && controller.LyricsWindow is null && !canvas.IsFrameDriverRunning && canvas.LayoutCount == 0, "close releases scenes and glyph caches");
            shell.Floating.MiniEnabled = true; shell.Floating.LyricsEnabled = true; Settle();
            Require(!ReferenceEquals(mini, controller.MiniWindow), "reopen constructs fresh released window");
            controller.Dispose(); Require(controller.MiniWindow is null && controller.LyricsWindow is null, "main lifetime closes both windows");
            Console.WriteLine("PASS floating windows: shared playback/lyrics, actual button and seek input, queue, themes, language, lock adapter, position reset, JSON persistence, no-lyrics/intro, hide/reopen/dispose. Audio and passthrough are fixtures.");
        }
        finally { controller.Dispose(); window.Close(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization(); Pump(session.DisposeAsync().AsTask()); }
        void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Wait(Func<bool> predicate)
        { while (!predicate()) { limit.Token.ThrowIfCancellationRequested(); Settle(); Thread.Sleep(2); } Settle(); }
        void Pump(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
        void Click(Window host, string name)
        {
            Settle(); var button = host.FindControl<Button>(name)!;
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), host)!.Value;
            host.MouseDown(point, MouseButton.Left); host.MouseUp(point, MouseButton.Left); Settle();
        }
        void Capture(Window host, string name)
        { Settle(); using var frame = host.CaptureRenderedFrame() ?? throw new Exception("no frame"); frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class PreviewLyrics : ILyricsSource
    {
        public int Reads; public bool HasLyrics = true;
        public async Task<LyricsText?> ReadAsync(string audioPath, string? explicitPath, CancellationToken cancellation)
        {
            Reads++; await Task.Delay(5, cancellation);
            return HasLyrics ? new("[00:02.00]月光洒落在你我之间\n[00:12.00]让回声留在这一刻\n[00:22.00]And let the music carry us home", "lrc", "fixture") : null;
        }
    }
    private sealed class PreviewNative : INativeAudio
    {
        private long operation;
        public event Action<string, JsonElement>? Notification;
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Position(int frames)
        {
            using var message = JsonDocument.Parse($"{{\"operationId\":{operation},\"framesPlayed\":{frames}}}");
            Notification?.Invoke("audio.position", message.RootElement.Clone());
        }
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            var json = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openFile" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":223}}",
                _ => "true"
            };
            using var document = JsonDocument.Parse(json); return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
