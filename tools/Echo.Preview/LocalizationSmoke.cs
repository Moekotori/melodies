using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.Settings.Models;
using Echo.Desktop.Localization;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Preferences;

internal static class LocalizationSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var text = new LocalizationService(); text.SetLanguage("zh-CN");
        var other = new LocalizationService(); other.SetLanguage("zh-CN");
        var shell = new ShellViewModel(text);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show(); shell.NavigateCommand.Execute("settings"); Dispatcher.UIThread.RunJobs();
            var combo = window.GetVisualDescendants().OfType<ComboBox>()
                .First(control => control.DataContext is SettingRowViewModel { Id: "language" });
            var row = (SettingRowViewModel)combo.DataContext!;
            Check(row.IsEnabled, "Language selector is unavailable.");
            combo.SelectedItem = row.Choices.First(choice => choice.Value == "en-US");
            Dispatcher.UIThread.RunJobs();
            Check(text.Preference == "en-US" && shell.Settings.ActiveCategory.Title == "General", "Language selection failed.");
            Check(window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "Settings"), "DynamicResource did not update visible text.");
            Check(other.Current.Code == "zh-CN", "Language leaked into another window.");
            var appearance = shell.LyricsAppearance;
            Check(appearance.StyleChoices[0].Label == "Minimal" && appearance.FontChoice?.Label == "Follow style", "Lyrics choices were not translated.");
            shell.OpenQuickSettingsCommand.Execute("lyrics"); Dispatcher.UIThread.RunJobs();
            var styleInput = window.GetVisualDescendants().OfType<ComboBox>().First(control => control.Name == "LyricStyleInput");
            styleInput.SelectedItem = appearance.StyleChoices[2];
            Check(appearance.SelectedStyle.Id == appearance.StyleChoices[2].Id, "Localized lyrics selector did not apply its stable ID.");
            var lyricsBefore = appearance.Snapshot();
            text.SetLanguage("zh-CN"); text.SetLanguage("en-US");
            Check(appearance.Snapshot() == lyricsBefore, "Language switch changed lyrics settings.");
            Dispatcher.UIThread.RunJobs(); Capture(window, Path.Combine(output, "lyrics-en.png"));
            shell.CloseQuickSettingsCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            Capture(window, Path.Combine(output, "settings-en.png"));
            shell.Settings.Query = "window";
            Check(shell.Settings.SearchResults.Count > 0, "Localized settings search failed.");
            shell.Settings.Query = "";
            shell.Settings.SelectCategoryCommand.Execute("appearance");
            var theme = shell.Settings.Sections.SelectMany(section => section.Rows).First(item => item.Id == "theme");
            var dark = theme.Choices.First(choice => choice.Value == "深色");
            Check(dark.Label == "Dark", "Choice label was not translated.");
            theme.SelectCommand.Execute(dark.Value);
            Check(shell.IsDark && theme.SelectedValue == "深色", "Translation changed the command value.");
            shell.IsDark = false;
            shell.NavigateCommand.Execute("home"); Dispatcher.UIThread.RunJobs();
            Check(shell.ActivePageTitle == "Home", "Localized navigation failed.");
            Capture(window, Path.Combine(output, "home-en.png"));
            var switchWatch = Stopwatch.StartNew();
            for (var index = 0; index < 20; index++) text.SetLanguage(index % 2 == 0 ? "zh-CN" : "en-US");
            Dispatcher.UIThread.RunJobs(); switchWatch.Stop();
            Check(window.Resources.MergedDictionaries.Count == 1, "Language resource dictionaries accumulated.");
            text.SetLanguage("zh-CN"); shell.NavigateCommand.Execute("settings"); shell.Settings.SelectCategoryCommand.Execute("general");
            Dispatcher.UIThread.RunJobs(); Capture(window, Path.Combine(output, "settings-zh.png"));
            Check(shell.Settings.ActiveCategory.Title == "通用", "Switching back failed.");
            VerifyPacks(text.Catalog);
            VerifyPreferences(output);
            var key = text.Catalog.SourceKeys["设置"];
            for (var index = 0; index < 10000; index++) _ = text[key];
            var watch = Stopwatch.StartNew();
            var before = GC.GetAllocatedBytesForCurrentThread();
            string? last = null;
            for (var index = 0; index < 1000000; index++) last = text[key];
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            watch.Stop(); Check(last == "设置" && allocated == 0, "Lookup allocated memory or returned incorrect text.");
            var result = new LocalizationMeasurements(text.Catalog.Packs.Keys.ToArray(), text.Catalog.Fallback.Strings.Count,
                1000000, watch.Elapsed.TotalMilliseconds, allocated, switchWatch.Elapsed.TotalMilliseconds,
                "Debug headless; not Release or device acceptance");
            var report = JsonSerializer.Serialize(result, LocalizationMeasurementsJson.Default.LocalizationMeasurements);
            File.WriteAllText(Path.Combine(output, "results.json"), report);
            Console.WriteLine(report);
            Console.WriteLine("Language selection, live resources, stable values, search, window isolation, fallback, pack validation and preferences passed.");
        }
        finally { window.Close(); shell.Settings.Dispose(); shell.ReleaseLocalization(); }
    }

    private static LanguagePack Pack(string json)
    { using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)); return LanguageCatalog.ReadPack(stream); }
    private static void VerifyPacks(LanguageCatalog catalog)
    {
        Check(catalog.Resolve("en-AU").Code == "en-US" && catalog.Resolve("unsupported").Code == "zh-CN", "Culture fallback failed.");
        var sparse = Pack("""{"code":"fr-FR","nativeName":"Français","strings":{"language.followSystem":"Système"}}""");
        var local = new LocalizationService(new LanguageCatalog([catalog.Fallback, sparse])); local.SetLanguage("fr-FR");
        Check(local["language.followSystem"] == "Système" && local.Format("queue.count", 2).Contains('2'), "Sparse pack fallback failed.");
        Check(local["missing.key"] == "missing.key", "Missing key diagnostics failed.");
        ExpectFailure(() => Pack("""{"code":"fr-FR","nativeName":"Français","strings":{"a":"x","a":"y"}}"""));
        ExpectFailure(() => new LanguageCatalog([catalog.Fallback, Pack("""{"code":"fr-FR","nativeName":"Français","strings":{"shell.pending":"Missing argument"}}""")]));
        ExpectFailure(() => new LanguageCatalog([catalog.Fallback, Pack("""{"code":"fr-FR","nativeName":"Français","strings":{"player.signal":"{5}"}}""")]));
        ExpectFailure(() => new LanguageCatalog([catalog.Fallback, Pack("""{"code":"fr-FR","nativeName":"Français","strings":{"unknown.key":"x"}}""")]));
        ExpectFailure(() => { using var tooLarge = new MemoryStream(new byte[256 * 1024 + 1]); LanguageCatalog.ReadPack(tooLarge); });
    }
    private static void VerifyPreferences(string output)
    {
        var path = Path.Combine(output, "preferences.json");
        var store = new JsonDesktopPreferencesStore(path);
        store.Save(new DesktopPreferences(Language: "en-US")); store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        var reader = new JsonDesktopPreferencesStore(path);
        try
        {
            Check(reader.ReadAsync(CancellationToken.None).GetAwaiter().GetResult()?.Language == "en-US", "Language preference did not persist.");
            File.WriteAllText(path, "{\"Version\":1}");
            Check(reader.ReadAsync(CancellationToken.None).GetAwaiter().GetResult()?.Language == "system", "Old preferences lost compatibility.");
        }
        finally { reader.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    private static void ExpectFailure(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or FormatException or ArgumentException) { return; }
        throw new InvalidOperationException("Invalid language pack was accepted.");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Capture(Window window, string path)
    {
        using var image = new RenderTargetBitmap(new PixelSize(1440, 900), new Vector(96, 96));
        image.Render(window); image.Save(path, PngBitmapEncoderOptions.Default);
    }
}

internal sealed record LocalizationMeasurements(string[] Languages, int Keys, int Lookups, double ElapsedMs,
    long AllocatedBytes, double TwentySwitchesWithFinalLayoutMs, string Mode);
[JsonSerializable(typeof(LocalizationMeasurements))]
internal partial class LocalizationMeasurementsJson : JsonSerializerContext;
