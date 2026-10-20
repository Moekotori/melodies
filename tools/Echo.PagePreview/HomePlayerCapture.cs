using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Home;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Shell;
using Echo.Playback;

internal static class HomePlayerCapture
{
    public static void Run(MainWindow window, ShellViewModel shell, IReadOnlyList<TrackRow> tracks, HomePlayerPlayback native, string output)
    {
        Until(() => window.GetVisualDescendants().OfType<HomeView>().Any(view => view.Model is { IsLoading: false, HasCollection: true }));
        Capture("idle");
        var track = tracks[0];
        var queued = new QueueTrack("home-player-preview", track.Id, track.Path, Metadata: new Dictionary<string, string?>
        { ["title"] = track.Title, ["artist"] = track.Artist, ["album"] = track.Album, ["format"] = "WAV", ["sampleRate"] = "96000", ["bitDepth"] = "24" });
        Wait(native.Runtime.LocalZone.Session.PlayAsync(queued)); native.Started();
        Until(() => shell.Player!.HasTrack && shell.Player.PlayIcon == "pause");
        native.Position(67); Pump(80); Capture("playing");
        var home = window.GetVisualDescendants().OfType<HomeView>().Single();
        if (home.Model!.ContinueIcon != "pause") throw new InvalidOperationException("Home transport did not follow confirmed playback.");
        Click(home.FindControl<Button>("ContinueButton")!);
        Until(() => native.Pauses > 0 && home.Model.ContinueIcon == "play"); Capture("home-paused");
        Wait(native.Runtime.LocalZone.Session.PlayAsync(queued)); native.Started();
        Until(() => shell.Player!.PlayIcon == "pause");
        var bar = window.GetVisualDescendants().OfType<PlayerBar>().Single();
        var previousPauses = native.Pauses;
        Click(bar.FindControl<Button>("PlayButton")!);
        Until(() => native.Pauses > previousPauses); Capture("paused");
        var seek = bar.FindControl<Slider>("SeekSlider")!;
        var seekPoint = seek.TranslatePoint(new(seek.Bounds.Width * .62, seek.Bounds.Height / 2), window)!.Value;
        window.MouseDown(seekPoint, MouseButton.Left); window.MouseUp(seekPoint, MouseButton.Left);
        Until(() => native.Seeks > 0); Capture("seek");
        foreach (var width in new[] { 1920, 1440, 1000, 900 })
        {
            window.Width = width;
            foreach (var dark in new[] { false, true })
            {
                shell.IsDark = dark; Pump(60);
                Capture($"{width}-{(dark ? "dark" : "light")}");
                var play = bar.FindControl<Button>("PlayButton")!;
                var cover = bar.FindControl<Button>("CoverButton")!;
                if (play.Bounds.Width < 44 || cover.Bounds.Width < 44) throw new InvalidOperationException("Player targets collapsed.");
            }
        }
        window.Width = 1440; shell.UiScalePercent = 125; Pump(60); Capture("scale-125");
        shell.UiScalePercent = 100; shell.Text.SetLanguage("en-US"); Pump(60); Capture("english");
        shell.Text.SetLanguage("zh-CN");
        var longest = tracks.MaxBy(item => item.Title.Length)!;
        var longTrack = queued with { ItemId = "long-preview", TrackId = longest.Id, FilePath = longest.Path, Metadata = new Dictionary<string, string?>
        { ["title"] = longest.Title, ["artist"] = longest.Artist, ["album"] = longest.Album } };
        Wait(native.Runtime.LocalZone.Session.PlayAsync(longTrack)); native.Started();
        Until(() => shell.Player!.Details.Title == longest.Title); Capture("long-title");
        window.Width = 1000; Pump(60); Capture("long-title-narrow");
        var volume = bar.FindControl<Button>("VolumeButton")!;
        Click(volume); Pump(60);
        if (volume.Flyout?.IsOpen != true) throw new InvalidOperationException("Volume popover did not open.");
        Capture("volume"); volume.Flyout.Hide();
        Console.WriteLine("Home/player rendered: idle, protocol-confirmed playback/pause, long titles, light/dark, 900/1000/1440/1920 px, 125% scale, English and volume input. No audio hardware was used.");

        void Capture(string name)
        {
            Pump(40);
            using var frame = window.CaptureRenderedFrame() ?? throw new IOException("No rendered frame.");
            frame.Save(Path.Combine(output, "home-player-" + name + ".png"), PngBitmapEncoderOptions.Default);
        }
        void Click(Button button)
        {
            var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump(30);
        }
    }
    private static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        do { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        while (clock.ElapsedMilliseconds < milliseconds);
    }
    private static void Until(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready()) { if (clock.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Home/player capture"); Pump(5); }
        Pump(20);
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
}
