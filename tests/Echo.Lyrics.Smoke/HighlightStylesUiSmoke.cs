using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Settings.Drawers;
using Echo.Desktop.Localization;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Lyrics;
using Echo.Playback;

internal static class HighlightStylesUiSmoke
{
    public static void Run()
    {
        var root = Path.GetFullPath("misc/lyrics-highlight-styles"); Directory.CreateDirectory(root);
        var watch = Stopwatch.StartNew();
        var audio = Path.Combine(root, "fixture.wav"); File.WriteAllBytes(audio, [0]);
        var lyric = Path.Combine(root, "fixture.ass");
        File.WriteAllText(lyric, "[Events]\nDialogue: 0,0:00:01.00,0:00:20.00,D,,0,0,0,,{\\k400}把这一刻 {\\k400}唱给你听 {\\k400}Echo {\\k400}forever");
        var shell = new ShellViewModel(); shell.Text.SetLanguage("zh-CN");
        var source = new LocalLyricsSource(); var session = new PlaybackSessionCoordinator(new TestHost());
        var player = new PlayerViewModel(new PlaybackZone("highlight-styles", "fixture", session), shell.Text);
        var path = Path.Combine(root, "isolated-settings.json");
        var store = new LocalLyricsPreferencesStore(path);
        var appearance = new LyricsAppearance(store, shell.Text) { FontSize = 42, Context = false, HideSongInfo = true };
        shell.AttachLyrics(source, appearance); shell.AttachPlayback(player);
        using var model = new LyricsViewModel(player, source, appearance);
        var motion = new MotionPolicy(shell.Text); motion.SelectedChoice = motion.Choices.First(c => c.Mode == MotionMode.Full);
        var viewport = new LyricsViewportControl(); viewport.Bind(model, motion);
        var window = new Window { Width = 920, Height = 420, Content = viewport, Background = Brushes.White };
        foreach (var pair in shell.Text.Current.Strings) window.Resources[pair.Key] = pair.Value;
        try
        {
            window.Show(); Pump(session.SetQueueAsync([new("fixture", "fixture", audio)], "fixture"));
            Pump(session.PauseAsync()); Pump(model.ImportAsync(lyric));
            var variants = new HashSet<string>();
            foreach (var id in LyricsHighlightCatalog.Ids)
            {
                appearance.WordHighlightStyle = id;
                Pump(session.SeekAsync(6)); var early = Capture(id + "-early");
                Pump(session.SeekAsync(7)); var middle = Capture(id);
                Check(early != middle, id + " follows confirmed time");
                Check(variants.Add(middle), id + " has distinct rendered pixels");
                var count = viewport.LayoutCount;
                appearance.WordHighlightStyle = id == "sweep" ? "wave" : "sweep";
                Check(viewport.LayoutCount == count, "style switch retains shaped text");
                appearance.WordHighlightStyle = id;
            }
            appearance.WordHighlight = false;
            Pump(session.SeekAsync(6)); var disabled = Capture("disabled");
            Pump(session.SeekAsync(7)); Check(disabled == Capture("disabled-later"), "disabled highlight is static");
            Check(viewport.LayoutCount <= 18 && !viewport.IsFrameDriverRunning, "paused renderer bounded and idle");
            appearance.WordHighlight = true; appearance.WordHighlightStyle = "wave";
            motion.SelectedChoice = motion.Choices.First(c => c.Mode == MotionMode.Reduced);
            var reduced = Capture("wave-reduced"); appearance.WordHighlightStyle = "fade";
            Check(reduced == Capture("fade-reduced"), "reduced motion removes spatial transforms");
            foreach (var id in new[] { "cassette", "blossom", "jizura", "afterglow", "curtain", "kineticPoster", "cutBoard" })
                Check(LyricsHighlightCatalog.Resolve("wave", appearance.Styles.First(s => s.Id == id).Layout) == "sweep", id + " owns highlight");

            var drawer = new LyricsSettingsDrawerView(shell); window.Content = new Border { Padding = new Thickness(20, 0), Child = drawer }; window.Width = 480; window.Height = 700; Tick();
            var input = drawer.FindControl<ComboBox>("LyricHighlightStyleInput")!;
            Check(input.ItemCount == 14, "actual drawer offers 14 styles");
            input.SelectedItem = appearance.HighlightChoices.First(c => c.Id == "lens"); Tick();
            Check(appearance.WordHighlightStyle == "lens", "drawer selection updates presentation");
            Capture("drawer-light"); input.IsDropDownOpen = true; Tick(); Capture("drawer-options"); input.IsDropDownOpen = false;
            window.RequestedThemeVariant = ThemeVariant.Dark; Tick();
            // This isolated Window does not install the shell's theme binding.
            window.Background = new SolidColorBrush(Color.Parse("#101014"));
            Capture("drawer-dark");
            appearance.WordHighlight = false; Tick(); Check(!input.IsEnabled, "picker disabled with highlight");
            appearance.WordHighlight = true;
            shell.Settings.SelectCategoryCommand.Execute("lyrics");
            shell.Settings.SelectTabCommand.Execute("逐字与同步");
            var row = shell.Settings.Sections.SelectMany(s => s.Rows).First(r => r.Id == "lyrics-highlight-style");
            Check(row.SelectedValue == "lens" && row.Choices.Count == 14 && row.IsEnabled, "full settings reflect choice");
            row.SelectedValue = "spark"; Check(appearance.WordHighlightStyle == "spark", "full settings updates appearance");
            shell.Text.SetLanguage("en-US"); Check(appearance.HighlightChoice?.Label == "Sparkle", "language change retains ID");
            appearance.WordHighlight = false;
            Pump(store.DisposeAsync().AsTask());
            var reader = new LocalLyricsPreferencesStore(path); var restored = new LyricsAppearance(reader, shell.Text);
            Pump(restored.RestoreAsync(CancellationToken.None));
            Check(restored.WordHighlightStyle == "spark" && !restored.WordHighlight, "real file restores style and independent toggle");
            restored.ReleaseLocalization(); Pump(reader.DisposeAsync().AsTask());
            var legacy = new LocalLyricsPreferencesStore(Path.Combine(root, "legacy.json"));
            File.WriteAllText(Path.Combine(root, "legacy.json"), "{\"Style\":\"default\",\"WordHighlight\":true}");
            var old = new LyricsAppearance(legacy); Pump(old.RestoreAsync(CancellationToken.None));
            Check(old.WordHighlightStyle == "sweep", "older settings preserve original sweep");
            old.ReleaseLocalization(); Pump(legacy.DisposeAsync().AsTask());
            drawer.Dispose();
            Console.WriteLine("PASS 14 distinct rendered styles, confirmed progress, toggle, reduced motion, own-highlight scenes, drawer + full settings, localization, persistence, legacy default and bounded idle renderer.");
        }
        finally
        {
            window.Close(); viewport.Dispose(); model.Dispose(); player.Detach();
            shell.Settings.Dispose(); shell.ReleaseLocalization(); motion.ReleaseLocalization();
            Pump(session.DisposeAsync().AsTask()); Pump(source.DisposeAsync().AsTask()); Pump(store.DisposeAsync().AsTask());
        }
        string Capture(string name)
        {
            Tick(); using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
            using var bytes = new MemoryStream(); image.Save(bytes, PngBitmapEncoderOptions.Default);
            var value = bytes.ToArray(); File.WriteAllBytes(Path.Combine(root, name + ".png"), value);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value));
        }
        void Pump(Task task)
        {
            while (!task.IsCompleted) { Check(watch.Elapsed < TimeSpan.FromSeconds(30), "UI timeout"); Tick(); Thread.Sleep(2); }
            task.GetAwaiter().GetResult(); Tick();
        }
    }
    private static void Tick() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
