using Avalonia;
using Avalonia.Controls;

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Shell;

// Real product views, isolated presentation state; no library or audio processes.
var builder = AppBuilder.Configure<SettingsReviewApp>().With(DesktopFonts.CreateOptions());
if (args.FirstOrDefault() == "--capture")
{
    builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
    var window = SettingsReviewApp.CreateWindow();
    var shell = (ShellViewModel)window.DataContext!;
    Console.WriteLine("Window created"); window.Show(); Console.WriteLine("Window shown"); Pump(); Console.WriteLine("First layout");
    foreach (var category in shell.Settings.NavigationGroups.SelectMany(group => group.Items))
    { Console.WriteLine(category.Id); shell.Settings.SelectCategoryCommand.Execute(category.Id); Pump(); }
    foreach (var dark in new[] { false, true })
    {
        shell.IsDark = dark;
        foreach (var category in new[] { "general", "appearance", "playback", "lyrics" })
        {
            shell.Settings.SelectCategoryCommand.Execute(category); Pump();
            Save(category + (dark ? "-dark" : "-light"));
        }
    }
    shell.IsDark = false; shell.Settings.SelectCategoryCommand.Execute("general");
    shell.Settings.Query = "侧栏";
    Pump(); Save("search-results");
    var result = shell.Settings.SearchResults.First(item => item.RowId == "sidebar-icon-only");
    shell.Settings.OpenResultCommand.Execute(result); Pump();
    if (shell.Settings.Query.Length != 0 || !shell.Settings.Sections.SelectMany(section => section.Rows).Any(row => row.IsHighlighted))
        throw new InvalidOperationException("Search jump failed.");
    Save("search-jump");
    shell.Settings.SelectCategoryCommand.Execute("appearance");
    window.Width = 900; window.Height = 760; Pump(); Save("appearance-narrow");
    shell.Settings.SelectCategoryCommand.Execute("general"); Pump(); Save("general-narrow");
    window.RequestExit(); shell.Settings.Dispose(); shell.ReleaseLocalization();
    Console.WriteLine("PASS category rendering, search jump, light/dark and narrow product views.");
    void Save(string name)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window); bitmap.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
else builder.UsePlatformDetect().StartWithClassicDesktopLifetime(args);

static void Pump()
{ Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }

sealed class SettingsReviewApp : App
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        { desktop.MainWindow = CreateWindow(); desktop.ShutdownMode = ShutdownMode.OnMainWindowClose; }
    }
    public static MainWindow CreateWindow()
    {
        var shell = new ShellViewModel();
        shell.Text.SetLanguage("zh-CN");
        shell.Settings.AttachUserLanguages(new Echo.Desktop.Features.Settings.UserLanguagesViewModel(shell.Text,
            new Echo.Desktop.Localization.UserLanguageStore(Path.Combine(Path.GetTempPath(), "echo-settings-preview-languages"))));
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == Echo.Desktop.Motion.MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900, Title = "ECHO · 设置界面预览" };
        shell.NavigateCommand.Execute("settings");
        return window;
    }
}
