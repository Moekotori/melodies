using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Shell;

internal static class MotionFilm
{
    public static void Record(MainWindow window, ShellViewModel model, string output)
    {
        var directory = Path.Combine(output, "frames");
        Directory.CreateDirectory(directory);
        var frame = 0;
        var frameWatch = new Stopwatch();
        void Capture(int count)
        {
            for (var i = 0; i < count; i++)
            {
                frameWatch.Restart();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No compositor frame captured.");
                bitmap.Save(Path.Combine(directory, $"motion-{frame++:000}.png"), PngBitmapEncoderOptions.Default);
                var remaining = 17 - (int)frameWatch.ElapsedMilliseconds;
                if (remaining > 0) Thread.Sleep(remaining);
            }
        }
        model.NavigateCommand.Execute("settings");
        Capture(36);
        var replay = window.GetVisualDescendants().OfType<Button>().First(x => Equals(x.Content, "再次播放"));
        replay.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Capture(36);
        var expand = window.GetVisualDescendants().OfType<Button>().First(x => Equals(x.Content, "展开 / 收起"));
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Capture(28);
        model.Notice = "动效预览：轻柔进入，快速操作跟随最新状态。";
        Capture(28);
        model.DismissNoticeCommand.Execute(null);
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Capture(28);
        model.NavigateCommand.Execute("home");
        Capture(28);
        Console.WriteLine($"Recorded {frame} native Avalonia motion frames.");
    }
}
