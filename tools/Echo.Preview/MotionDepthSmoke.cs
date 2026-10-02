using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Desktop.Motion;

internal static class MotionDepthSmoke
{
    public static void Run(string output)
    {
        var policy = new MotionPolicy();
        policy.SelectedChoice = policy.Choices.First(x => x.Mode == MotionMode.Full);
        var tile = new Border { Name = "momentum-probe", Width = 40, Height = 40, Background = Brushes.Red };
        var window = new Window { Width = 240, Height = 160, Background = Brushes.Black, Content = tile };
        MotionScope.SetPolicy(window, policy);
        using var lane = new MotionLane();
        window.Show(); Pump(40);
        _ = lane.PlayAsync(tile, 1, 1, MotionLane.At(x: 40), MotionLane.At(), TimeSpan.FromMilliseconds(220));
        Pump(35);
        var before = MotionLane.Current(tile).Value.M31;
        Pump(4);
        var position = MotionLane.Current(tile).Value.M31;
        Require(position < before, "Original movement did not advance.");
        lane.Cancel();
        _ = lane.PlayAsync(tile, 1, 1, MotionLane.At(x: position), MotionLane.At(x: 40), TimeSpan.FromMilliseconds(180));
        Pump(24);
        var continued = MotionLane.Current(tile).Value.M31;
        Require(continued < position, $"Reversal discarded velocity: before={before:F3}, position={position:F3}, continued={continued:F3}.");
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "momentum-reversal.png"), PngBitmapEncoderOptions.Default);
        Pump(330);
        Require(!lane.IsRunning && tile.RenderTransform!.Value.M31 == 40, "Momentum reversal did not settle.");
        for (var i = 0; i < 12; i++)
            _ = lane.PlayAsync(tile, 0, 1, MotionLane.At(8), MotionLane.At(), TimeSpan.FromMilliseconds(180));
        lane.Cancel(); Pump(35);
        Require(!lane.IsRunning, "Pre-commit cancellation retained an animation.");
        window.Close();
        Console.WriteLine($"Momentum/cancellation proof: before={before:F2}, reverse start={position:F2}, continued={continued:F2}; settled at 40px.");
        RunHosts(output);
    }
    private static void RunHosts(string output)
    {
        var policy = new MotionPolicy();
        policy.SelectedChoice = policy.Choices.First(x => x.Mode == MotionMode.Full);
        var host = new MotionPresenter();
        host.ContentRetired += content => ((ProbePage)content).Dispose();
        var first = new ProbePage(Brushes.Red);
        var second = new ProbePage(Brushes.Blue);
        var third = new ProbePage(Brushes.Green);
        host.Content = first;
        var window = new Window { Width = 480, Height = 280, Background = Brushes.Black, Content = host };
        MotionScope.SetPolicy(window, policy);
        window.Show(); Pump(30);
        host.Content = second; Pump(30);
        Require(first.Disposals == 0 && host.Children.Count == 2, "Retiring page lost resources before its exit.");
        Require(host.Children.First().IsEnabled && !host.Children.First().IsHitTestVisible,
            "Retiring page must retain its appearance and stop input.");
        host.Content = third; Pump(30);
        Require(first.Disposals == 1 && second.Disposals == 0 && host.Children.Count == 2,
            "Latest navigation did not retire only the superseded page.");
        Pump(330);
        Require(second.Disposals == 1 && third.Disposals == 0 && host.Children.Count == 1,
            "Final frame did not release its retiring page exactly once.");
        host.Content = null; host.Settle();
        Require(third.Disposals == 1, "Immediate close did not release the current page.");
        var surface = new MotionSurface { Axis = MotionAxis.Horizontal, Travel = 160, Child = new Border { Background = Brushes.Red } };
        window.Content = surface;
        surface.IsOpen = true; Pump(30);
        Require(MotionLane.Current(surface).Value.M31 > 0 && MotionLane.Current(surface).Value.M32 == 0,
            "Horizontal drawer did not move on its own axis.");
        surface.IsOpen = false; Pump(20); surface.IsOpen = true; Pump(330);
        Require(surface.IsVisible && !surface.IsTransitioning && surface.Opacity == 1, "Drawer reversal did not settle.");
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "horizontal-drawer.png"), PngBitmapEncoderOptions.Default);
        surface.IsOpen = false;
        policy.SelectedChoice = policy.Choices.First(x => x.Mode == MotionMode.Off); Pump(15);
        Require(!surface.IsVisible && !surface.IsTransitioning, "Disabling motion left a drawer alive.");
        window.Close(); policy.ReleaseLocalization();
        Console.WriteLine("Retirement/drawer proof passed: appearance, bounded trees, deferred/exactly-once disposal, axis, reversal and off mode.");
    }
    private sealed class ProbePage(IBrush brush) : Control, IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
        public override void Render(DrawingContext context)
        { context.FillRectangle(brush, new Rect(Bounds.Size)); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    internal static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(4); }
        Dispatcher.UIThread.RunJobs();
    }
}
