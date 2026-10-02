using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Queue;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playback;

// Native UI and real isolated SQLite; audio acknowledgements are a deterministic fixture.
internal static class PlayerBarSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var clock = Stopwatch.StartNew();
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var opening = LibraryDatabase.OpenAsync(Path.Combine(output, "library.sqlite"), token.Token);
        Pump(opening); var database = opening.Result;
        var native = new PreviewNative();
        var runtime = new Echo.Application.Playback.PlaybackRuntime(native);
        // The window shares this runtime and must not stop it on close.
        var window = DesktopComposition.CreateWindow(false, false, database, playback: runtime);
        var shell = (ShellViewModel)window.DataContext!;
        // Also supports already-built Desktop versions from before the window locale binding.
        var previewStrings = new ResourceDictionary();
        using (var catalog = JsonDocument.Parse(File.ReadAllText("src/Echo.Desktop/Resources/Languages/zh-CN.json")))
            foreach (var item in catalog.RootElement.GetProperty("strings").EnumerateObject()) previewStrings.Add(item.Name, item.Value.GetString());
        window.Resources.MergedDictionaries.Add(previewStrings);
        var player = shell.Player;
        var playback = player.Session;
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        try
        {
            var folder = Path.Combine(output, "fixture"); Directory.CreateDirectory(folder);
            var first = Path.Combine(folder, "first.flac"); File.WriteAllBytes(first, []);
            var second = Path.Combine(folder, "second.flac"); File.WriteAllBytes(second, []);
            File.Copy("src/Echo.Desktop/Resources/Artwork/placeholder-light.png", Path.Combine(folder, "cover.png"), true);
            var start = database.BeginScanAsync(folder, false, token.Token); Pump(start);
            Pump(database.CommitScanBatchAsync(start.Result, 1,
                [Entry(first, "Moonlight Echo", "ECHO Orchestra"), Entry(second, "Second Song", "Second Artist")], token.Token));
            var lookup = database.FindTrackByPathAsync(first, token.Token); Pump(lookup);
            if (lookup.Result is { Favorite: true } saved)
            {
                Pump(database.SetTrackFavoriteAsync(saved.Id, false, token.Token));
                lookup = database.FindTrackByPathAsync(first, token.Token); Pump(lookup);
            }
            Require(lookup.Result is { Favorite: false, SampleRate: 96000 }, "indexed current track query");
            window.Show(); Settle(); Capture("empty-light.png");
            foreach (var button in window.GetVisualDescendants().OfType<Button>())
                if (button.Flyout is Flyout { Content: Control content } && content is not ThemeVariantScope)
                    foreach (var key in previewStrings.Keys.OfType<string>().Where(x => x.StartsWith("ui.Player", StringComparison.Ordinal)))
                        content.Resources[key] = previewStrings[key];
            Pump(playback.SetQueueAsync([new("a", lookup.Result!.Id, first), new("b", "b", second)], "a", token.Token));
            Pump(playback.PauseAsync(token.Token));
            Wait(() => player.Details.CanFavorite && shell.AlbumArtwork!.Count > 0, "current track/artwork");
            Require(player.Details.Title == "Moonlight Echo" && player.Details.Artist == "ECHO Orchestra" && player.Details.Specifications.Contains("96"), "track metadata");
            Click("FavoriteButton"); Wait(() => player.Details.IsFavorite, "favorite UI command");
            var favorite = database.FindTrackByPathAsync(first, token.Token); Pump(favorite);
            Require(favorite.Result!.Favorite, "favorite persisted by shared writer");
            var slider = Find<Slider>("SeekSlider");
            Require(slider is Echo.Desktop.Controls.PlayerWaveformSlider, "waveform is the default active seek control");
            Require(window.GetVisualDescendants().OfType<Echo.Desktop.Controls.SignalPathStateIcon>().Count() == 1, "current star signal icon");
            var from = slider.TranslatePoint(new Point(slider.Bounds.Width * .2, 12), window)!.Value;
            var to = slider.TranslatePoint(new Point(slider.Bounds.Width * .65, 12), window)!.Value;
            var seeks = native.Seeks;
            window.MouseDown(from, MouseButton.Left); window.MouseMove(to); Settle();
            Require(player.IsSeeking && native.Seeks == seeks, "seek is preview only during drag");
            window.MouseUp(to, MouseButton.Left); Wait(() => native.Seeks == seeks + 1 && !player.IsSeeking, "seek commits once");
            Require(Math.Abs(slider.Value - playback.Snapshot.PositionSeconds) < .1, "confirmed seek position");
            Require(Math.Abs(slider.Value / slider.Maximum - .65) < .01, "waveform pointer fraction matches playback fraction");
            slider.Focus(); window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Wait(() => native.Seeks == seeks + 2, "keyboard seeking");
            var clickPoint = slider.TranslatePoint(new Point(slider.Bounds.Width * .15, 12), window)!.Value;
            window.MouseDown(clickPoint, MouseButton.Left); window.MouseUp(clickPoint, MouseButton.Left);
            Wait(() => native.Seeks == seeks + 3, "waveform click seeks once");
            Require(Math.Abs(slider.Value / slider.Maximum - .15) < .01, "waveform click uses the full strip");
            Capture("player-light.png"); shell.IsDark = true; Settle(); Capture("player-dark.png");
            shell.IsDark = false; Settle();
            Click("VolumeButton"); Settle();
            var volumeRoot = (Control)((Flyout)Find<Button>("VolumeButton").Flyout!).Content!;
            var volumePanel = volumeRoot as PlayerVolumePanel ?? volumeRoot.GetVisualDescendants().OfType<PlayerVolumePanel>().Single();
            volumePanel.DataContext = shell;
            var volumeSlider = volumePanel.FindControl<Slider>("VolumeSlider")!;
            volumeSlider.Focus(); window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            window.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            Wait(() => player.Volume < 1 && !player.IsChangingVolume, "volume key command");
            Capture("volume-light.png"); shell.IsDark = true; Settle(); Capture("volume-dark.png");
            if (volumeRoot is ThemeVariantScope scope) Require(scope.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark, "popup follows dark theme while open");
            shell.IsDark = false; Settle(); Find<Button>("VolumeButton").Flyout!.Hide();
            Click("SignalPathButton"); Settle(); Capture("signal-path.png"); Find<Button>("SignalPathButton").Flyout!.Hide();
            Click("OutputSettingsButton"); Settle(); Capture("more-menu.png"); Find<Button>("OutputSettingsButton").Flyout!.Hide();
            var menuRoot = (Control)((Flyout)Find<Button>("OutputSettingsButton").Flyout!).Content!;
            var panelMenu = menuRoot as StackPanel ?? menuRoot.GetVisualDescendants().OfType<StackPanel>().First();
            Require(panelMenu.Children.OfType<Button>().Any(x => x.Content?.ToString() == "打开音频文件"), "more menu localization resources loaded");
            foreach (var width in new[] { 1180, 980, 800 })
            {
                window.Width = width; Settle();
                var cover = Find<Button>("CoverButton"); var next = Find<Button>("NextButton"); var volume = Find<Button>("VolumeButton");
                var coverRight = cover.TranslatePoint(new Point(cover.Bounds.Width, 0), window)!.Value.X;
                var transportLeft = Find<StackPanel>("Transport").TranslatePoint(default, window)!.Value.X;
                var nextRight = next.TranslatePoint(new Point(next.Bounds.Width, 0), window)!.Value.X;
                var volumeLeft = volume.TranslatePoint(default, window)!.Value.X;
                Require(coverRight <= transportLeft && nextRight < volumeLeft && volumeLeft + volume.Bounds.Width <= width, "narrow layout does not overlap");
                Capture($"player-{width}.png");
            }
            shell.UiScalePercent = 150; Settle(); Capture("player-800-scale150.png"); shell.UiScalePercent = 100;
            window.Width = 1440; Settle();
            Pump(playback.NextAsync(token.Token)); Wait(() => player.Details.Title == "Second Song", "next-track metadata replacement");
            Require(!player.Details.IsFavorite, "favorite from previous track is cleared");
            Pump(playback.PreviousAsync(token.Token)); Wait(() => player.Details.IsFavorite, "favorite restored from database");
            Click("CoverButton"); Require(shell.ActivePageTitle == "歌词", "cover opens lyrics");
            Console.WriteLine($"PASS player bar: real SQLite favorite, indexed metadata, artwork, seek drag/keyboard, volume keyboard, navigation, next/previous, light/dark, narrow renders. {clock.Elapsed.TotalSeconds:0.0}s. Audio is simulated.");
        }
        finally
        {
            window.Close(); Wait(() => !window.IsVisible, "window cleanup");
            Pump(runtime.DisposeAsync().AsTask()); Pump(database.DisposeAsync().AsTask());
        }

        ScanEntry Entry(string path, string title, string artist) => new(new(path, LibraryPath.Key(path), 0, 1), ScanEntryKind.Metadata,
            new(title, artist, "Migration Preview", artist, 223, "flac", 96000, 24));
        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(x => x.Name == name);
        void Click(string name)
        { var button = Find<Button>(name); var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value; window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle(); }
        void Capture(string name)
        {
            Settle(); using var bitmap = window.CaptureRenderedFrame() ?? throw new IOException("Player render unavailable");
            bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
            if (name is "player-light.png" or "player-dark.png")
            {
                var bar = window.GetVisualDescendants().OfType<PlayerBar>().Single();
                using var detail = new RenderTargetBitmap(new PixelSize((int)bar.Bounds.Width, (int)bar.Bounds.Height));
                detail.Render(bar); detail.Save(Path.Combine(output, "bottom-" + name), PngBitmapEncoderOptions.Default);
            }
        }
        void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Wait(Func<bool> check, string message)
        { while (!check()) { token.Token.ThrowIfCancellationRequested(); Settle(); Thread.Sleep(2); } Settle(); }
        void Pump(Task task) { Wait(() => task.IsCompleted, "task completion"); task.GetAwaiter().GetResult(); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class PreviewNative : INativeAudio
    {
        private long operation;
        public int Seeks { get; private set; }
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            if (method == "audio.seek") Seeks++;
            var json = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openFile" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":223}}",
                _ => "true"
            };
            using var doc = JsonDocument.Parse(json); return Task.FromResult(doc.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
