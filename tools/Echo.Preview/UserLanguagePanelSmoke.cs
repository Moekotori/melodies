using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Settings;
using Echo.Desktop.Localization;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

internal static class UserLanguagePanelSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var text = new LocalizationService(); text.SetLanguage("zh-CN");
        var store = new UserLanguageStore(Path.Combine(output, "languages-" + Guid.NewGuid().ToString("N")));
        using var model = new UserLanguagesViewModel(text, store);
        var shell = new ShellViewModel(text); shell.Settings.AttachUserLanguages(model);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        shell.NavigateCommand.Execute("settings");
        var window = new MainWindow { DataContext = shell };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Require(window.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "ImportLanguageButton" && button.IsEnabled), "Settings import entry point is unavailable.");
            using (var input = Input("""{"code":"en-US","nativeName":"Personal English","strings":{"language.user.title":"My language packs"}}""")) Pump(model.ImportAsync(input));
            Require(text.Preference == "en-US" && text.Current.IsUser && text["navigation.home"] == "Home", "Import/select/fallback failed.");
            Require(window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "My language packs"), "Settings did not display custom text.");
            Capture(window, Path.Combine(output, "settings-custom-en.png"));
            var imported = Directory.GetFiles(store.DirectoryPath, "*.json").Single();
            File.WriteAllText(imported, """{"code":"en-US","nativeName":"Personal English","strings":{"language.user.title":"Reloaded text"}}""");
            var reload = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ReloadLanguagesButton");
            reload.Command!.Execute(null); Pump(model.ReloadCommand.ExecutionTask!);
            Require(text["language.user.title"] == "Reloaded text", "Reload button did not apply edited JSON.");
            File.WriteAllText(Path.Combine(store.DirectoryPath, "broken.json"), "{bad"); Pump(model.ReloadAsync());
            Require(model.HasIssues && text["language.user.title"] == "Reloaded text", "Invalid file broke a valid custom pack.");
            Capture(window, Path.Combine(output, "settings-custom-error.png"));
            using (var exported = new MemoryStream())
            { Pump(model.ExportAsync(exported)); exported.Position = 0; Require(LanguageCatalog.ReadPack(exported).Strings.Count == text.Catalog.Fallback.Strings.Count, "Export template is incomplete."); }
            using (var french = Input("""{"code":"fr-FR","nativeName":"Français","strings":{"language.user.title":"Mes langues"}}""")) Pump(model.ImportAsync(french));
            Require(text.Preference == "fr-FR", "New language import failed.");
            text.ReplaceCatalog(LanguageCatalog.Default);
            Require(text.Preference == "fr-FR" && text.Languages.Any(choice => choice.Code == "fr-FR"), "Saved custom culture disappeared before discovery.");
            Pump(model.ReloadAsync()); Require(text.Current.Code == "fr-FR", "Discovery did not recover the saved culture.");
            Console.WriteLine("Actual settings panel: user import/select/fallback, live custom text, reload button, malformed-file feedback, export and saved-ID discovery passed.");
        }
        finally { window.Close(); shell.Settings.Dispose(); shell.ReleaseLocalization(); }
        Pump(UserLanguageStoreSmoke.RunAsync(Path.Combine(output, "core")));
    }
    private static MemoryStream Input(string json) => new(Encoding.UTF8.GetBytes(json));
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Pump(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        { if (watch.Elapsed.TotalSeconds > 20) throw new TimeoutException("User language check exceeded 20 seconds."); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Dispatcher.UIThread.RunJobs(); task.GetAwaiter().GetResult();
    }
    private static void Capture(Window window, string path)
    {
        Dispatcher.UIThread.RunJobs();
        using var image = new RenderTargetBitmap(new PixelSize(1440, 900), new Vector(96, 96)); image.Render(window); image.Save(path, PngBitmapEncoderOptions.Default);
    }
}
