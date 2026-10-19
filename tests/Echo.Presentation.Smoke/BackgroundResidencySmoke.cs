using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Contracts.Lyrics;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Features.Floating;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Desktop.Theming;
using Echo.Playback;
using Echo.Visuals;
using SkiaSharp;

// Exercise production visibility and native-resource ownership with small, isolated headless fixtures.
internal static class BackgroundResidencySmoke
{
    public static void Run()
    {
        var native = new NativeFixture();
        var session = new PlaybackSessionCoordinator(native);
        var shell = new ShellViewModel();
        var player = new PlayerViewModel(new PlaybackZone("fixture", "fixture", session), shell.Text);
        shell.AttachPlayback(player);
        using var lyrics = new LyricsViewModel(player, new EmptyLyrics(), shell.LyricsAppearance);
        try
        {
            Clock(player, session, native);
            Pointer(shell, lyrics);
            Wallpaper();
            Scene();
            Console.WriteLine("PASS background resources: hidden/restored clock, floating pointer timers and layout reuse, wallpaper pixels/source lifetime, scene retirement");
        }
        finally
        {
            player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization();
            Pump(session.DisposeAsync().AsTask());
        }
    }

    private static void Clock(PlayerViewModel player, PlaybackSessionCoordinator session, NativeFixture native)
    {
        var window = new Window();
        using var display = new PlayerWindowPresentation(window, player);
        try
        {
            var track = new QueueTrack("item", "track", "fixture.flac", new("http", "https://fixture/audio"));
            Pump(session.SetQueueAsync([track], track.ItemId));
            window.Show();
            native.Position(10); Wait(() => player.Position == 10);
            window.Hide(); var before = player.Position;
            native.Position(20); Wait(() => session.Snapshot.PositionSeconds == 20);
            Check(player.Position == before, "hidden clock does not update presentation");
            window.Show(); Wait(() => player.Position == 20);
            window.WindowState = WindowState.Minimized;
            native.Position(30); Wait(() => session.Snapshot.PositionSeconds == 30);
            Check(player.Position == 20, "minimized clock does not update presentation");
            window.WindowState = WindowState.Normal; Wait(() => player.Position == 30);
        }
        finally { window.Close(); }
    }

    private static void Pointer(ShellViewModel shell, LyricsViewModel lyrics)
    {
        var motion = new MotionPolicy(shell.Text);
        var window = new DesktopLyricsWindow(shell, lyrics, motion);
        try
        {
            shell.Floating.LyricsLocked = true;
            window.UsePassthrough((_, _) => true);
            window.Show(); window.ApplyLock(true);
            // Headless platforms may not provide a native handle; isolate visibility from OS passthrough support.
            Call(window, "UpdatePointerPolling", true);
            Check(Field<DispatcherTimer>(window, "poll").IsEnabled, "locked visible lyrics polls pointer");
            Call(window, "ShowChrome", true, true);
            var grid = window.FindControl<Grid>("Root")!;
            var rows = grid.RowDefinitions; var cursor = window.Cursor;
            Call(window, "ShowChrome", true, true);
            Check(ReferenceEquals(rows, grid.RowDefinitions) && ReferenceEquals(cursor, window.Cursor), "unchanged chrome reuses layout and cursor");
            window.Hide();
            Check(!Field<DispatcherTimer>(window, "poll").IsEnabled && !Field<DispatcherTimer>(window, "unlockHide").IsEnabled,
                "automatically hidden lyrics stops pointer timers");
            window.Show(); Call(window, "UpdatePointerPolling", true);
            Check(Field<DispatcherTimer>(window, "poll").IsEnabled, "shown locked lyrics resumes pointer polling");
            window.WindowState = WindowState.Minimized;
            Check(!Field<DispatcherTimer>(window, "poll").IsEnabled, "minimized lyrics stops polling");
        }
        finally { window.Close(); motion.ReleaseLocalization(); }
    }

    private static void Wallpaper()
    {
        var viewport = new WallpaperViewport();
        var window = new Window { Content = viewport };
        var names = new NameScope(); names.Register("ThemeWallpaperLayer", viewport); NameScope.SetNameScope(window, names);
        var type = typeof(PlayerViewModel).Assembly.GetType("Echo.Desktop.Bootstrap.ThemeWallpaper")!;
        var wallpaper = (IDisposable)Activator.CreateInstance(type, window, new ThemeViewModel())!;
        try
        {
            using var pixels = new SKBitmap(4, 4, SKColorType.Bgra8888, SKAlphaType.Premul);
            pixels.Erase(new SKColor(120, 80, 40, 255));
            using var png = SKImage.FromBitmap(pixels); using var data = png.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            var source = new Bitmap(stream);
            Call(wallpaper, "Remember", source);
            Check(ReferenceEquals(source, viewport.Image), "neutral wallpaper displays original decoded image");
            Call(wallpaper, "NoteBrightness", .5d);
            Wait(() => !ReferenceEquals(source, viewport.Image));
            using (var graded = WallpaperGrade.Snapshot(viewport.Image!))
                Check(graded.GetPixel(0, 0) == new SKColor(60, 40, 20, 255), "brightness preserves existing pixel calculation");
            for (var i = 0; i < 12; i++) Call(wallpaper, "NoteBrightness", .6d + i * .01);
            Call(wallpaper, "NoteBrightness", 1d);
            Wait(() => ReferenceEquals(source, viewport.Image));
            // A worker lease must outlive source replacement and final display cleanup.
            var retained = new WallpaperStillImage(new Bitmap(new MemoryStream(data.ToArray())));
            var lease = retained.Retain(); retained.Dispose();
            using (var copy = WallpaperGrade.Snapshot(lease.Image)) Check(copy.GetPixel(0, 0).Red == 120, "retained source remains readable");
            lease.Dispose();
            Call(wallpaper, "ClearMedia");
            Check(viewport.Image is null, "clearing media releases displayed resources");
        }
        finally { wallpaper.Dispose(); window.Close(); }
    }

    private static void Scene()
    {
        var motion = new MotionPolicy();
        using var scene = new JizuraSceneControl();
        using var pixels = new SKBitmap(4, 4);
        using var pool = new RasterFramePool();
        var frame = pool.Capture(pixels)!;
        using var second = pool.Capture(pixels)!;
        using var third = pool.Capture(pixels)!;
        Set(scene, "motion", motion); Set(scene, "active", true); Set(scene, "resourcesResident", true); Set(scene, "frame", frame);
        motion.IsWindowVisible = false; Call(scene, "UpdateResidency");
        Check(scene.HasFrame && Field<DispatcherTimer>(scene, "retirement").IsEnabled, "quick hide retains warm frame");
        motion.IsWindowVisible = true; Call(scene, "UpdateResidency");
        Check(scene.HasFrame && !Field<DispatcherTimer>(scene, "retirement").IsEnabled, "quick restoration cancels retirement");
        motion.IsWindowVisible = false; Call(scene, "RetireScene", null, EventArgs.Empty);
        Check(!scene.HasFrame, "parked scene releases its display frame");
        using var reclaimed = pool.Capture(pixels);
        Check(reclaimed is not null, "retirement returns the native frame slot");
        motion.ReleaseLocalization();
    }

    private static void Pump(Task task) => QueueProjectionSmoke.Pump(task);
    private static void Wait(Func<bool> ready) => QueueProjectionSmoke.Wait(ready);
    private static void Check(bool condition, string message) => QueueProjectionSmoke.Check(condition, message);
    private static object? Call(object value, string method, params object?[] args)
        => value.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(value, args);
    private static T Field<T>(object value, string field) => (T)value.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
    private static void Set(object value, string field, object fieldValue) => value.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(value, fieldValue);
    private sealed class EmptyLyrics : ILyricsSource
    { public Task<LyricsText?> ReadAsync(string audioPath, string? explicitPath, CancellationToken cancellation) => Task.FromResult<LyricsText?>(null); }
    private sealed class NativeFixture : INativeAudio
    {
        private int operation;
        public event Action<string, JsonElement>? Notification;
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken token = default)
        {
            var json = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openFile" or "audio.openSource" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":200}}",
                _ => "true"
            };
            using var document = JsonDocument.Parse(json); return Task.FromResult(document.RootElement.Clone());
        }
        public void Position(int seconds)
        {
            using var json = JsonDocument.Parse($"{{\"operationId\":{operation},\"framesPlayed\":{seconds * 48000}}}");
            Notification?.Invoke("audio.position", json.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
