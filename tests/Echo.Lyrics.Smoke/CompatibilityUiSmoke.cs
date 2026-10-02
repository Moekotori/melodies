using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Lyrics;
using Echo.Playback;

static class CompatibilityUiSmoke
{
    public static void Run()
    {
        var watch = Stopwatch.StartNew();
        var root = Path.GetFullPath("misc/lyrics-compatibility/ui"); Directory.CreateDirectory(root);
        var audio = Path.Combine(root, "preview.wav"); File.WriteAllBytes(audio, [0]);
        File.WriteAllText(Path.ChangeExtension(audio, ".qrc"), "[offset:100]\n[1000,3000]夜色沿着窗边流动(1000,3000)\n[5000,3000]把(5000,500)这一刻(5500,900)唱给(6400,800)你听(7200,800)\n[9000,3000]让旋律带着我们远行(9000,3000)");
        File.WriteAllText(Path.Combine(root, "preview.t.lrc"), "[offset:100]\n[00:01]The night flows past the window\n[00:05]Sing this moment to you\n[00:09]Let the melody carry us away");
        File.WriteAllText(Path.Combine(root, "preview.rom.lrc"), "[offset:100]\n[00:01]ye se yan zhe chuang bian liu dong\n[00:05]ba zhe yi ke chang gei ni ting\n[00:09]rang xuan lv dai zhe wo men yuan xing");
        var source = new LocalLyricsSource(); var session = new PlaybackSessionCoordinator(new TestHost());
        var player = new PlayerViewModel(new PlaybackZone("lyrics-compatibility", "歌词适配", session));
        var appearance = new LyricsAppearance { OffsetMs = 200, FontSize = 44 };
        var shell = new ShellViewModel(); shell.AttachPlayback(player); shell.AttachLyrics(source, appearance);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Off);
        Pump(session.SetQueueAsync([new("fixture", "fixture", audio, Metadata: new Dictionary<string, string?>
        { ["title"] = "此刻的回声", ["artist"] = "ECHO 歌词适配" })], "fixture"));
        Pump(session.PauseAsync()); Pump(session.SeekAsync(6.2));
        var view = new LyricsView(shell);
        var window = new Window { Width = 1280, Height = 800, Content = view }; window.Show();
        Wait(() => view.Model.HasLyrics && !view.Model.IsLoading);
        var line = view.Model.Document.Lines[1];
        Check(line.Words.Count == 4 && line.Translation == "Sing this moment to you"
            && line.Romanization == "ba zhe yi ke chang gei ni ting", "real view loads QRC and independent companion files");
        Capture("qrc-with-companions.png");
        var viewport = view.FindControl<LyricsViewportControl>("Viewport")!;
        Check(viewport.LayoutCount <= 18 && !viewport.IsFrameDriverRunning, "bounded paused rendering");
        Pump(view.Model.SeekLineAsync(1));
        Check(Math.Abs(session.Snapshot.PositionSeconds - 4.7) < .001, "line seek subtracts both file and display offsets");
        var imported = Path.Combine(root, "custom.lrc"); File.WriteAllText(imported, "[00:01]手动导入歌词");
        Pump(view.Model.ImportAsync(imported));
        Check(shell.LyricsSelection.ForTrack(audio) == imported && view.Model.Document.Lines[0].Text == "手动导入歌词", "explicit import is remembered by audio path");
        window.Close();
        Check(view.Model.Document.Lines.Count == 0 && !viewport.IsFrameDriverRunning, "detach releases lyrics and frame driver");
        view = new LyricsView(shell); window = new Window { Width = 900, Height = 640, Content = view }; window.Show();
        Wait(() => view.Model.HasLyrics && !view.Model.IsLoading);
        Check(view.Model.Document.Lines[0].Text == "手动导入歌词", "reopening restores current-track import");
        var bad = Path.Combine(root, "too-large.lrc"); File.WriteAllText(bad, new string('x', 1024 * 1024 + 1));
        Pump(view.Model.ImportAsync(bad));
        Check(!view.Model.IsLoading && !view.Model.HasLyrics && view.Model.Message.Length > 0, "oversized import shows recovery state without unhandled task failure");
        Pump(view.Model.LoadAsync(null));
        Check(view.Model.HasLyrics, "reload recovers the last successful import");
        window.Close(); CancellationSmoke.Run(Pump);
        player.Detach(); Pump(session.DisposeAsync().AsTask()); Pump(source.DisposeAsync().AsTask()); shell.LyricsAppearance.ReleaseLocalization();
        Console.WriteLine($"PASS local compatibility UI: load/render, dual-offset seek, import/reopen, failure/recovery, stale results and detach ({watch.ElapsedMilliseconds} ms)");

        void Capture(string name)
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame() ?? throw new Exception("lyrics frame missing");
            frame.Save(Path.Combine(root, name), PngBitmapEncoderOptions.Default);
        }
        void Pump(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
        void Wait(Func<bool> ready)
        {
            while (!ready())
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("lyrics UI short test timed out");
                Dispatcher.UIThread.RunJobs(); Thread.Sleep(5);
            }
            Dispatcher.UIThread.RunJobs();
        }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
}
