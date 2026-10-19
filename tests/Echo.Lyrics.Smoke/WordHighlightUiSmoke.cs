using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Features.Floating;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Infrastructure.Lyrics;
using Echo.Playback;

static class WordHighlightUiSmoke
{
    public static void Run()
    {
        var watch = Stopwatch.StartNew();
        var root = Path.GetFullPath("misc/lyrics-word-highlight"); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "words.ass");
        var audio = Path.Combine(root, "fixture.wav"); File.WriteAllBytes(audio, [0]);
        File.WriteAllText(path, "[Events]\nDialogue: 0,0:00:01.00,0:00:08.00,D,,0,0,0,,{\\k200}你好 {\\k300}world");
        var source = new LocalLyricsSource(); var session = new PlaybackSessionCoordinator(new TestHost());
        var player = new PlayerViewModel(new PlaybackZone("word-highlight", "Word highlight", session));
        var appearance = new LyricsAppearance { FontSize = 44 };
        using var model = new LyricsViewModel(player, source, appearance);
        var motion = new MotionPolicy(); motion.SelectedChoice = motion.Choices.First(c => c.Mode == MotionMode.Off);
        var viewport = new LyricsViewportControl(); viewport.Bind(model, motion);
        var window = new Window { Width = 1000, Height = 700, Content = viewport }; window.Show();
        Pump(session.SetQueueAsync([new("words", "words", audio)], "words"));
        Pump(session.PauseAsync()); Pump(model.ImportAsync(path));
        Check(model.Document.Lines[0].Words.Count == 2, "ASS import reaches actual view model");
        foreach (var style in new[] { "default", "folded", "roseVinyl" })
        {
            appearance.SelectedStyle = appearance.Styles.Single(item => item.Id == style);
            window.Background = new SolidColorBrush(appearance.SelectedStyle.Background.Length == 0
                ? Colors.White : Color.Parse(appearance.SelectedStyle.Background));
            Pump(session.SeekAsync(1.1)); var before = Capture(style + "-before");
            Pump(session.SeekAsync(4)); var after = Capture(style + "-after");
            Check(!before.SequenceEqual(after), style + " pixels reflect native-confirmed word progress");
            appearance.WordHighlight = false;
            Pump(session.SeekAsync(1.1)); var disabledBefore = Capture(style + "-disabled-before");
            Pump(session.SeekAsync(4)); var disabledAfter = Capture(style + "-disabled-after");
            Check(disabledBefore.SequenceEqual(disabledAfter), style + " disabled word highlighting stays static");
            appearance.WordHighlight = true;
        }
        Check(viewport.LayoutCount <= 18 && !viewport.IsFrameDriverRunning, "paused highlighter has bounded layouts and no timer");
        var single = model.Document.Lines[0] with { Words = [new("你好 world", 1000, 3000)] };
        Check(Math.Abs(DesktopLyricsLayout.Fill(single, null, 2000, 8000, true) - .5) < .001, "floating single-word timing");
        window.Close(); viewport.Dispose(); player.Detach();
        Pump(session.DisposeAsync().AsTask()); Pump(source.DisposeAsync().AsTask());
        motion.ReleaseLocalization(); appearance.ReleaseLocalization();
        Console.WriteLine("PASS rendered word highlights: ASS import, three styles, on/off, paused native position, single-word desktop timing and disposal.");

        byte[] Capture(string name)
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame() ?? throw new Exception("missing frame");
            using var stream = new MemoryStream(); frame.Save(stream, PngBitmapEncoderOptions.Default);
            var bytes = stream.ToArray(); File.WriteAllBytes(Path.Combine(root, name + ".png"), bytes); return bytes;
        }
        void Pump(Task task)
        {
            while (!task.IsCompleted)
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException("word highlight UI");
                Dispatcher.UIThread.RunJobs(); Thread.Sleep(2);
            }
            task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        }
    }
    static void Check(bool value, string name) { if (!value) throw new Exception(name); }
}
