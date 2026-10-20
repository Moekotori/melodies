using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Remote;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

internal static class RemoteMotionChecks
{
    public static void Run(Window window, RemoteView view, ShellViewModel shell, string output)
    {
        var model = view.Model;
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Full);
        model.AddCommand.Execute(null); Pump(320); Save("remote-landing-full.png");
        model.ChooseProviderCommand.Execute("webdav"); Pump(80); Save("remote-opening.png");
        Pump(320); Save("remote-connected-layout.png");
        Check(model.ShowConnection && !model.ShowBrowser, "Connection workspace owns interaction");
        var picker = view.GetVisualDescendants().OfType<RemoteProviderPicker>().Single();
        var editor = view.GetVisualDescendants().OfType<RemoteConnectionEditor>().Single();
        var landing = view.GetVisualDescendants().OfType<RemoteConnectionLanding>().Single();
        Check(editor.Bounds.Width > 200 && picker.Bounds.Width < editor.Bounds.Width, "Provider rail and form share wide layout");
        Choose("jellyfin"); Pump(35);
        var priorOpacity = MotionLane.CurrentOpacity(editor);
        Choose("onedrive"); Pump(10);
        Check(MotionLane.CurrentOpacity(editor) >= priorOpacity - 0.08, "Interrupted form fade keeps its current opacity");
        foreach (var id in new[] { "webdav", "emby", "smb", "sshfs", "onedrive", "jellyfin", "subsonic" })
        { Choose(id); Pump(15); }
        model.CloseEditorCommand.Execute(null); Pump(15);
        model.ChooseProviderCommand.Execute("subsonic"); Pump(350);
        Check(model.EditorOpen && model.DraftProvider == "subsonic" && editor.Opacity == 1, "Rapid switches settle at latest provider");
        Check(!landing.IsTransitioning, "Provider lanes release after rapid switches");
        window.Width = 900; Pump(320); Save("remote-connection-compact.png");
        Check(editor.TranslatePoint(default, view)!.Value.Y > picker.TranslatePoint(default, view)!.Value.Y,
            "Compact form flows below provider choices");
        model.ChooseProviderCommand.Execute("webdav"); Pump(15);
        window.WindowState = WindowState.Minimized; Pump(30);
        Check(!shell.Motion.IsWindowVisible, "Minimize reaches window motion policy");
        Check(view.GetVisualDescendants().OfType<MotionSurface>().All(surface => !surface.IsTransitioning), "Hidden window settles surfaces");
        Check(!landing.IsTransitioning, "Hidden window releases provider and form lanes");
        window.WindowState = WindowState.Normal;
        window.Hide(); Pump(15);
        Check(!shell.Motion.IsWindowVisible && !landing.IsTransitioning, "Actual window hide settles animation owners");
        // Finish the shell's own reopen scene before testing a different global motion mode.
        // This probe owns Remote's animations, not the concurrently edited page presenter.
        window.Show(); Pump(350);
        Check(shell.Motion.IsWindowVisible, "Actual window show restores motion policy");
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Reduced);
        model.ChooseProviderCommand.Execute("jellyfin"); Pump(120);
        Check(editor.RenderTransform!.Value == Matrix.Identity && editor.Opacity == 1, "Reduced motion settles form without displacement");
        model.CloseEditorCommand.Execute(null); model.CloseProviderPickerCommand.Execute(null);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        window.Width = 1440; Pump(30);
        Check(model.ShowBrowser && !model.ShowConnection, "Closing add workspace restores browser");
        Console.WriteLine("PASS remote motion: rapid pointer choices, opacity continuity, full/reduced/off, compact layout and hidden-window settling.");

        void Choose(string id)
        {
            var button = picker.GetVisualDescendants().OfType<Button>().Single(item => item.Tag as string == id);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Check(model.DraftProvider == id && model.EditorOpen, "Pointer chooses provider " + id);
        }

        void Save(string name)
        {
            using var frame = window.CaptureRenderedFrame() ?? throw new IOException("Missing remote motion frame");
            frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
    }
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); }
    private static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(5);
        } while (clock.ElapsedMilliseconds < milliseconds);
    }
}
