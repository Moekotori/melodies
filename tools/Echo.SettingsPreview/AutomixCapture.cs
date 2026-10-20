using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Settings.Controls;
using Echo.Desktop.Features.Settings.Models;
using Echo.Desktop.Shell;

internal static class AutomixCapture
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var window = SettingsReviewApp.CreateWindow();
        var shell = (ShellViewModel)window.DataContext!;
        try
        {
            window.Show(); shell.Settings.SelectCategoryCommand.Execute("playback");
            shell.Settings.SelectTabCommand.Execute("播放偏好"); Pump();
            if (FindRow().DataContext is not SettingRowViewModel { Badge: "ALPHA" })
                throw new Exception("Preview loaded an older desktop assembly without Alpha settings.");
            foreach (var dark in new[] { false, true })
            {
                shell.IsDark = dark; Pump();
                var row = FindRow();
                var toggle = row.GetVisualDescendants().OfType<ToggleSwitch>().Single();
                var point = toggle.TranslatePoint(new Avalonia.Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window)!.Value;
                var previous = ((SettingRowViewModel)row.DataContext!).BoolValue;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
                if (((SettingRowViewModel)row.DataContext!).BoolValue == previous) throw new Exception("Alpha toggle did not respond.");
                Capture("automix-" + (dark ? "dark" : "light"));
            }
            window.Width = 900; window.Height = 760; Pump(); Capture("automix-compact");
            Console.WriteLine("PASS real Alpha settings row, toggle input, light/dark and compact capture");
        }
        finally { window.RequestExit(); shell.Settings.Dispose(); shell.ReleaseLocalization(); }

        SettingRowView FindRow() => window.GetVisualDescendants().OfType<SettingRowView>()
            .Single(row => row.DataContext is SettingRowViewModel { Id: "automix" });
        void Capture(string name)
        {
            FindRow().BringIntoView(); Pump();
            using var image = window.CaptureRenderedFrame() ?? throw new Exception("No rendered settings frame.");
            image.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
        }
    }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
}
