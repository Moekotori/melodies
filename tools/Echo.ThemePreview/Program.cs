using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Localization;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

// Native product views and real input; only an isolated empty database and an idle audio fixture.
var output = Path.GetFullPath(args.FirstOrDefault() ?? "misc/semi-theme/previews");
Directory.CreateDirectory(output);
AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
Require(Application.Current!.Styles.OfType<Semi.Avalonia.SemiTheme>().Count() == 1, "Semi theme owner");
var text = new LocalizationService(); text.SetLanguage("zh-CN");
var shell = new ShellViewModel(text);
var opening = Echo.Infrastructure.Library.LibraryDatabase.OpenAsync(Path.Combine(output, "empty-" + Guid.NewGuid().ToString("N") + ".sqlite"));
Finish(opening);
var database = opening.GetAwaiter().GetResult();
var artwork = new Echo.Desktop.Features.Albums.AlbumArtworkCache(new Echo.Infrastructure.Artwork.LocalAlbumArtwork());
shell.AttachLibrary(database, artwork);
var session = new Echo.Playback.PlaybackSessionCoordinator(new IdleNative());
var player = new Echo.Desktop.Features.Player.PlayerViewModel(new Echo.Playback.PlaybackZone(Echo.Playback.PlaybackZone.LocalId, "本机", session), text);
shell.AttachPlayback(player);
shell.Motion.SelectedChoice = shell.Motion.Choices.First(c => c.Mode == MotionMode.Off);
var main = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
try
{
    main.Show(); Pump();
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (main.GetVisualDescendants().OfType<Echo.Desktop.Features.Home.HomeView>().Single().Model!.IsLoading)
    { Require(DateTime.UtcNow < deadline, "empty library load"); Pump(); Thread.Sleep(2); }
    Capture(main, "home-light.png");
    shell.Theme.Mode = "深色"; Pump(); Capture(main, "home-dark.png");
    shell.Theme.Mode = "浅色"; shell.NavigateCommand.Execute("settings");
    shell.Settings.SelectCategoryCommand.Execute("appearance"); Pump(); Capture(main, "settings-light.png");
    shell.Theme.Mode = "深色"; Pump(); Capture(main, "settings-dark.png");
    shell.Settings.SelectCategoryCommand.Execute("general"); Pump(); Capture(main, "settings-general-dark.png");
    var toggle = main.GetVisualDescendants().OfType<ToggleSwitch>().First(t => t.IsEnabled);
    var checkedBefore = toggle.IsChecked; Click(toggle, main);
    Require(toggle.IsChecked != checkedBefore, "settings switch input");
    shell.Settings.SelectCategoryCommand.Execute("appearance"); Pump();
    text.SetLanguage("en-US"); Pump(); Capture(main, "settings-english.png");
    main.Width = 900; main.Height = 760; Pump(); Capture(main, "settings-narrow.png");
    foreach (var dark in new[] { false, true }) Controls(dark);
    Console.WriteLine("PASS: native home/settings, light/dark, English/narrow; Semi button click/disabled, ComboBox popup/keyboard selection.");
}
finally
{
    main.RequestExit(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization();
    Finish(session.DisposeAsync().AsTask()); Finish(artwork.DisposeAsync().AsTask()); Finish(database.DisposeAsync().AsTask());
}

void Controls(bool dark)
{
    shell.Theme.Mode = dark ? "深色" : "浅色"; Pump();
    var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    var clicks = 0;
    var button = new Button { Content = "刷新曲库" };
    button.Click += (_, _) => clicks++;
    var primary = new Button { Content = "开始播放", Classes = { "primary" } };
    var combo = new ComboBox { ItemsSource = new[] { "跟随系统", "浅色", "深色" }, SelectedIndex = 0, Width = 220 };
    var input = new TextBox { PlaceholderText = "搜索曲名、艺术家或专辑", Width = 300 };
    var stack = new StackPanel { Margin = new Thickness(32), Spacing = 20, Children = {
        new TextBlock { Text = "ECHO · Semi", FontSize = 24, FontWeight = FontWeight.SemiBold },
        new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { button, primary } },
        input, combo, new CheckBox { Content = "显示歌词翻译", IsChecked = true },
        new ToggleSwitch { Content = "启用通知", IsChecked = true },
        new Slider { Minimum = 0, Maximum = 100, Value = 65, Width = 300 }
    } };
    var palette = new ResourceDictionary();
    foreach (var entry in (ResourceDictionary)main.Resources.ThemeDictionaries[variant]) palette[entry.Key] = entry.Value;
    var window = new Window { Width = 680, Height = 580, Content = stack, RequestedThemeVariant = variant,
        Background = (IBrush)palette["EchoField"]!, Resources = palette };
    try
    {
        window.Show(); Pump();
        Require(!button.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "PART_ButtonBorder"), "ordinary button uses Semi template");
        Click(button, window); Require(clicks == 1, "button input");
        button.IsEnabled = false; Pump(); Click(button, window); Require(clicks == 1, "disabled input suppression");
        button.IsEnabled = true; Pump();
        Capture(window, dark ? "controls-dark.png" : "controls-light.png");
        Click(combo, window); Require(combo.IsDropDownOpen, "popup opens");
        Capture(window, dark ? "dropdown-dark.png" : "dropdown-light.png");
        Press(window, Key.Down, PhysicalKey.ArrowDown); Press(window, Key.Enter, PhysicalKey.Enter);
        Require(combo.SelectedIndex == 1 && !combo.IsDropDownOpen, "popup keyboard selection");
    }
    finally { window.Close(); }
}
void Capture(Window window, string name)
{
    Pump(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
    bitmap.Render(window); bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
}
static void Click(Control control, Window window)
{
    var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
}
static void Press(Window window, Key key, PhysicalKey physical)
{ window.KeyPress(key, RawInputModifiers.None, physical, null); window.KeyRelease(key, RawInputModifiers.None, physical, null); Pump(); }
static void Pump()
{ Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
static void Require(bool value, string message)
{ if (!value) throw new InvalidOperationException(message); }
static void Finish(Task task)
{
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!task.IsCompleted) { Require(DateTime.UtcNow < deadline, "preview task deadline"); Pump(); Thread.Sleep(2); }
    task.GetAwaiter().GetResult();
}

sealed class IdleNative : Echo.Playback.INativeAudio
{
    public event Action<string, System.Text.Json.JsonElement>? Notification { add { } remove { } }
    public event Action<Exception>? Failed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<System.Text.Json.JsonElement> CallAsync(string method, System.Text.Json.JsonElement? parameters = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Idle UI preview must not issue audio commands.");
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
