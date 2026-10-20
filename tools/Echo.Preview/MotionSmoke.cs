using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

internal static class MotionSmoke
{
    public static void Run(MainWindow window, ShellViewModel model, string output)
    {
        model.Motion.SelectedChoice = model.Motion.Choices.First(x => x.Mode == MotionMode.Full);
        var page = window.FindControl<MotionPresenter>("PageHost")!;
        Pump(280);
        var retired = page.Content!;
        model.NavigateCommand.Execute("songs");
        Pump(35);
        Require(page.IsTransitioning, "Navigation failed to start its transition.");
        var entering = page.Children.Last();
        Require(MotionLane.CurrentOpacity(entering) < 1, "Incoming page timeline did not interpolate its opacity.");
        // Home → songs moves down the sidebar, so the page enters from the right.
        Require(MotionLane.Current(entering).Value.M31 > 0, "Incoming page timeline did not interpolate its forward slide.");
        Require(!page.Children.First().IsHitTestVisible, "Outgoing page still receives input.");
        model.NavigateCommand.Execute("albums");
        model.NavigateCommand.Execute("settings");
        Pump(20);
        Require(page.Children.Count <= 2, "Rapid navigation retained extra views.");
        Require(!retired.GetVisualAncestors().Any(), "Superseded page did not detach.");
        Pump(280);
        Require(page.Children.Count == 1 && !page.IsTransitioning, "Navigation did not settle and release its old page.");
        Save(window, Path.Combine(output, "motion-settings-light.png"));

        // Retarget a surface while entering, then disable motion during its exit.
        var notice = window.FindControl<MotionSurface>("NoticeSurface")!;
        model.Notice = "动画生命周期短测";
        Pump(25);
        Require(notice.IsTransitioning && notice.IsVisible, "Notice did not enter.");
        model.DismissNoticeCommand.Execute(null);
        Pump(15);
        Require(!notice.IsHitTestVisible && notice.IsVisible, "Retiring notice must remain drawn but inert.");
        model.Notice = "最新提示";
        Pump(260);
        Require(notice.IsVisible && notice.Opacity == 1 && !notice.IsTransitioning, "Notice reversal did not settle.");
        model.DismissNoticeCommand.Execute(null);
        model.Motion.SelectedChoice = model.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        Pump(20);
        Require(!notice.IsVisible && !notice.IsTransitioning, "Disabling motion left a stale notice.");
        model.NavigateCommand.Execute("home");
        Pump(20);
        Require(page.Children.Count == 1 && !page.IsTransitioning, "Disabled motion still retained an outgoing page.");
        var action = window.GetVisualDescendants().OfType<Button>().First(x => x.IsEnabled);
        Require(action.Transitions is null, "Disabled motion left feedback transitions active.");

        model.Motion.SelectedChoice = model.Motion.Choices.First(x => x.Mode == MotionMode.Reduced);
        model.NavigateCommand.Execute("settings");
        Pump(20);
        var reduced = page.Children.Last().RenderTransform!.Value;
        Require(reduced.M31 == 0 && reduced.M32 == 0, "Reduced motion still translates the page.");
        Pump(130);
        Require(window.GetVisualDescendants().OfType<ToggleSwitch>().All(x => x.KnobTransitions.Count == 0),
            "Reduced motion retained switch knob movement.");
        model.IsDark = true;
        Save(window, Path.Combine(output, "motion-settings-dark.png"));

        // Popups share the owning window policy and release in-progress exits on hide.
        model.Motion.SelectedChoice = model.Motion.Choices.First(x => x.Mode == MotionMode.Full);
        var popupButton = window.GetVisualDescendants().OfType<Button>().First(x => x.Flyout is MotionFlyout);
        var flyout = (MotionFlyout)popupButton.Flyout!;
        flyout.ShowAt(popupButton);
        Pump(45);
        Require(flyout.IsOpen && flyout.IsTransitioning, "Flyout did not animate on open.");
        Require(ReferenceEquals(MotionScope.GetPolicy(flyout.Popup.Child!), model.Motion), "Flyout lost the window policy.");
        flyout.Hide();
        Pump(15);
        Require(!flyout.Popup.Child!.IsHitTestVisible, "Closing flyout still receives input.");
        flyout.ShowAt(popupButton);
        Pump(260);
        Require(flyout.IsOpen && !flyout.IsTransitioning && flyout.Popup.Child!.Opacity == 1, "Flyout reverse did not settle.");
        model.NavigateCommand.Execute("home");
        Pump(30);
        window.Hide();
        Pump(25);
        Require(!page.IsTransitioning && page.Children.Count == 1 && !flyout.IsOpen, "Hidden window retained visual work.");
        window.Show();
        Pump(420);
        Require(!page.IsTransitioning && page.Children.Single().Opacity == 1, "Restored window did not reach a valid state.");

        model.Motion.SystemAllowsAnimation = false;
        model.Motion.SelectedChoice = model.Motion.Choices.First(x => x.Mode == MotionMode.System);
        model.NavigateCommand.Execute("songs");
        Pump(20);
        Require(!page.IsTransitioning && action.Transitions is null, "System animation preference was ignored.");
        model.Motion.SystemAllowsAnimation = null;
        model.Motion.SelectedChoice = model.Motion.Choices.First(x => x.Mode == MotionMode.Full);
        model.IsDark = false;
        model.NavigateCommand.Execute("home");
        Pump(260);
        MotionFilm.Record(window, model, output);
        Console.WriteLine("Motion smoke passed: interpolation, latest navigation, retiring input, detach, surfaces, off/reduced/system modes, popup reversal and hide/restore.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(8);
        }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Save(MainWindow window, string path)
    {
        Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(new PixelSize(1440, 900), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        Console.WriteLine(path);
    }
}
