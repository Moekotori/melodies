using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Playback;

internal static class QueueVisualCapture
{
    public static void Run(string output, string data)
    {
        Directory.CreateDirectory(output);
        using var document = JsonDocument.Parse(File.ReadAllText(data));
        var tracks = document.RootElement.EnumerateArray().Take(20).Select((entry, i) =>
        {
            var track = entry.GetProperty("track");
            string Value(string key) => track.TryGetProperty(key, out var field) && field.ValueKind != JsonValueKind.Null ? field.ToString() : "";
            return new QueueTrack($"reference-{i}", Value("id"), Value("path"), new("http", "http://127.0.0.1/preview-only"),
                new Dictionary<string, string?> { ["title"] = Value("title"), ["artist"] = Value("artist"), ["album"] = Value("album"),
                    ["codec"] = Value("codec"), ["duration"] = Value("duration"), ["bitDepth"] = Value("bitDepth"), ["sampleRate"] = Value("sampleRate") },
                Origin: new("manual", "手动添加"));
        }).ToArray();
        var duration = double.Parse(tracks[0].Metadata!["duration"]!, System.Globalization.CultureInfo.InvariantCulture);
        var session = new PlaybackSessionCoordinator(new QueueUiSmoke.PreviewNative(duration));
        var shell = new ShellViewModel();
        var player = new PlayerViewModel(new PlaybackZone("local", "本机", session), shell.Text);
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork());
        shell.AttachPlayback(player); player.Queue.Artwork = artwork;
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        var watch = Stopwatch.StartNew();
        try
        {
            window.Show();
            Pump(session.SetQueueAsync(tracks, tracks[0].ItemId)); Pump(session.PauseAsync());
            shell.NavigateCommand.Execute("queue");
            foreach (var dark in new[] { false, true }) { shell.IsDark = dark; Settle(); Save(dark ? "queue-reference-dark.png" : "queue-reference-light.png"); }
            shell.ToggleQueueDrawerCommand.Execute(null); Settle(); Save("queue-reference-drawer.png"); shell.CloseQueueDrawerCommand.Execute(null);
            window.Width = 900; window.Height = 720; Settle(); Save("queue-reference-compact.png");
            Console.WriteLine("Captured actual Avalonia controls with the same read-only track sample as original renderer. Playback is simulated; these are visual references.");
        }
        finally { player.Detach(); window.Close(); shell.Settings.Dispose(); Pump(session.DisposeAsync().AsTask()); Pump(artwork.DisposeAsync().AsTask()); }
        void Save(string name)
        {
            using var image = window.CaptureRenderedFrame() ?? throw new IOException("Queue capture missing");
            image.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); Console.WriteLine(Path.Combine(output, name));
        }
        void Pump(Task task)
        {
            while (!task.IsCompleted) { if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("Queue capture"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            task.GetAwaiter().GetResult();
            for (var i = 0; i < 4; ++i) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); }
        }
        void Settle()
        {
            for (var i = 0; i < 8; ++i) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(3); }
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        }
    }
}
