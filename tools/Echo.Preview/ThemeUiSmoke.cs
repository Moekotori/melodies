using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Settings;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

internal static class ThemeUiSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var shell = new ShellViewModel();
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show(); shell.NavigateCommand.Execute("settings"); shell.Settings.SelectCategoryCommand.Execute("appearance"); Pump();
            var editor = window.GetVisualDescendants().OfType<ThemeSettingsView>().Single();
            Check(editor.GetVisualDescendants().OfType<TextBox>().Any(), "editor entry");
            Save("theme-window-light.png"); shell.Theme.Mode = "深色"; shell.Theme.EditTone = "深色"; Pump(); Save("theme-window-dark.png");
            Check(shell.IsDark, "actual window dark mode");
            shell.Theme.Invert = true; Pump();
            var previous = shell.IsDark; shell.ToggleThemeCommand.Execute(null); Pump(); Check(shell.IsDark != previous, "titlebar toggle with inverted mode");
            shell.Theme.Invert = false; shell.Theme.Mode = "浅色";
            shell.Theme.SelectedPreset = Echo.Desktop.Theming.ThemeCatalog.Find("sakuraMilk"); Pump(); Save("theme-window-sakura.png");
            window.Width = 900; window.Height = 760; Pump(); Save("theme-window-narrow.png");
            Console.WriteLine("Theme editor entry, actual shell themes, inverted titlebar toggle and native light/dark/preset/narrow renders passed.");
        }
        finally { window.RequestExit(); shell.Settings.Dispose(); shell.ReleaseLocalization(); }
        void Save(string name)
        { Pump(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height)); bitmap.Render(window); bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); }
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    }
}
