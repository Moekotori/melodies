using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Echo.Desktop;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

internal static class BackgroundEmptyUiSmoke
{
    public static void Run(LyricsView view, MainWindow window, ShellViewModel shell, string root,
        Action<Task> pump, Action<string> capture)
    {
        window.Width = 640; window.Height = 360;
        var appearance = view.Model.Appearance;
        appearance.SelectedStyle = appearance.Styles[0];
        appearance.BackgroundMode = "coverPond";
        var empty = Path.Combine(root, "empty.lrc"); File.WriteAllText(empty, "");
        pump(view.Model.ImportAsync(empty));
        Check(!view.Model.HasLyrics && view.Model.Message.Length == 0, "absent lyrics leave footer empty");
        capture("background-no-lyrics");
        var background = view.FindControl<LyricsBackgroundSceneControl>("BackgroundScene")!;
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Full);
        Check(background.IsFrameDriverRunning, "visible background resumes animation");
        var sequence = background.FrameSequence;
        for(var i=0;i<12;i++){AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();Thread.Sleep(20);}
        Dispatcher.UIThread.RunJobs();
        Check(background.FrameSequence > sequence, "frame driver delivers new pictures");
        shell.Motion.IsWindowVisible = false;
        Check(!background.IsFrameDriverRunning && !background.HasFrame, "hidden background stops and releases frames");
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Off);
        shell.Motion.IsWindowVisible = true;
        capture("background-restored");
        Check(!background.IsFrameDriverRunning && background.HasFrame, "restored static frame respects motion policy");
        Console.WriteLine("PASS empty-lyrics render, background animation, hidden release and restore (headless software; GPU FPS excluded)");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

}
