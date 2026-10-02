using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Echo.Application.Playback;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Settings.Runtime;
using Echo.Desktop.Shell;
using Echo.Desktop.Localization;
using Echo.Desktop.Theming;
using Echo.Playback;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var root = Path.GetFullPath(args.FirstOrDefault() ?? "misc/playback-tools/previews");
Directory.CreateDirectory(root);
var native = new FixtureNative();
var session = new PlaybackSessionCoordinator(native);
var timer = new SleepTimerService(session);
var packs = new[] { "zh-CN", "en-US" }.Select(code =>
{
    using var file = File.OpenRead(Path.Combine("src/Echo.Desktop/Resources/Languages", code + ".json"));
    return LanguageCatalog.ReadPack(file);
});
var text = new LocalizationService(new LanguageCatalog(packs)); text.SetLanguage("zh-CN");
var preferences = new FeaturePreferences();
var tools = new PlayerToolsViewModel(session, timer, preferences, text);
var runtime = new SettingsFeatureRuntime(preferences);
_ = new PlaybackToolsSettingsModule(runtime, tools, timer, session);
var panel = new PlayerToolsPanel { DataContext = tools };
var content = new Border { Padding = new Thickness(24), Child = panel };
var window = new Window { Width = 390, Height = 660, Content = content };
var preset = ThemeCatalog.Find("classic");
var count = 0;
try
{
    ApplyLanguage(); ApplyTheme(false); window.Show(); Pump();
    var firstPath = Path.Combine(root, "fixture-a.wav"); var secondPath = Path.Combine(root, "fixture-b.wav");
    File.WriteAllBytes(firstPath, new byte[44]); File.WriteAllBytes(secondPath, new byte[44]);
    var a = new QueueTrack("a", "a", firstPath); var b = new QueueTrack("b", "b", secondPath);
    Wait(session.SetQueueAsync([a, b], "a")); native.Started(session.Snapshot.OperationId); Until(() => tools.CanLoop);
    tools.StartSeconds = 10; tools.EndSeconds = 20;
    Click("StartLoop"); Until(() => tools.LoopActive && !tools.IsBusy);
    Require(session.Queue.Count == 2 && native.LastQueue.GetProperty("items").GetArrayLength() == 1, "loop keeps UI queue and suppresses native advance");
    var oldOperation = session.Snapshot.OperationId;
    native.Position(oldOperation, 11); Until(() => native.SeekCalls == 2);
    Require(session.Snapshot.PositionSeconds == 10, "confirmed native boundary seeks A");
    native.Position(oldOperation, 90); Pump(); Thread.Sleep(25); Pump();
    Require(native.SeekCalls == 2, "stale pre-seek clock cannot repeat seek");
    Wait(session.PauseAsync()); native.Position(session.Snapshot.OperationId, 25); Pump();
    Require(native.SeekCalls == 2, "paused clock cannot advance loop");
    Capture("tools-light.png"); ApplyTheme(true); Pump(); Capture("tools-dark.png");
    Wait(session.SeekAsync(40)); Until(() => !tools.LoopActive);
    Require(native.LastQueue.GetProperty("items").GetArrayLength() == 2, "outside seek restores full native queue");
    native.RejectQueue = true;
    try { Wait(session.SetSegmentLoopAsync("a", 10, 20)); throw new Exception("expected loop rejection"); }
    catch (IOException) { }
    Require(session.SegmentLoop is null && native.LastQueue.GetProperty("items").GetArrayLength() == 2, "failed enable restores queue without confirming loop");
    Wait(session.SetSegmentLoopAsync("a", 10, 20)); Wait(session.NextAsync()); Until(() => !tools.LoopActive);
    Require(session.Snapshot.Current?.ItemId == "b", "manual next clears loop");
    Wait(session.SetSegmentLoopAsync("b", 10, 20)); Wait(session.StopAsync()); Until(() => !tools.LoopActive);
    Require(session.Snapshot.Status == PlaybackStatus.Stopped, "stop clears loop");
    Wait(session.ResumeAsync()); native.Started(session.Snapshot.OperationId); Until(() => tools.CanLoop);
    Wait(session.SetSegmentLoopAsync("b", 10, 20)); Wait(session.SelectQueueItemAsync("b")); Until(() => !tools.LoopActive);
    Require(native.LastQueue.GetProperty("items").GetArrayLength() == 2, "same-item reopen clears loop and restores native queue");
    tools.Minutes = 45; tools.ActionIndex = 1; Click("StartTimer"); Until(() => tools.TimerActive);
    Require(timer.State.Action == SleepTimerAction.Stop && timer.State.RemainingSeconds > 2600, "UI submits selected duration and action");
    window.Content = null; Pump();
    Require(timer.State.Active, "hidden panel leaves background timer running");
    Wait(timer.CancelAsync()); Pump();
    var saved = preferences.Snapshot(); tools.Minutes = 15; preferences.Restore(saved);
    Require(tools.Minutes == 45 && tools.ActionIndex == 1 && !timer.State.Active, "restore retains defaults but never restarts deadline");
    Wait(timer.StartAsync(.001, SleepTimerAction.Pause)); Until(() => !timer.State.Active && !timer.State.Executing && native.PauseCalls >= 2, 2200);
    Require(session.Snapshot.Status == PlaybackStatus.Paused, "expiry uses confirmed pause");
    var pauses = native.PauseCalls; Wait(timer.StartAsync(.001, SleepTimerAction.Pause)); Wait(timer.CancelAsync());
    Thread.Sleep(1100); Pump(); Require(native.PauseCalls == pauses, "cancel prevents expiry");
    native.RejectPause = true; Wait(timer.StartAsync(.001, SleepTimerAction.Pause)); Until(() => timer.State.Error is not null, 2200);
    Require(!timer.State.Active && !timer.State.Executing, "failed action ends once and surfaces error");
    native.RejectPause = false; Wait(session.RecoverAsync());
    var quits = 0; timer.SetQuitHandler(() => { Interlocked.Increment(ref quits); return Task.CompletedTask; });
    Wait(timer.StartAsync(.001, SleepTimerAction.Quit)); Until(() => quits == 1, 2200);
    Require(session.Snapshot.Status == PlaybackStatus.Stopped, "quit stops playback before requesting shutdown");
    window.Content = content;
    Wait(tools.CancelTimerCommand.ExecuteAsync(null));
    text.SetLanguage("en-US"); ApplyLanguage(); ApplyTheme(false); Pump(); Capture("tools-english.png");
    Require(panel.FindControl<ComboBox>("TimerAction")!.SelectedItem?.ToString() == "Stop when finished" && tools.ActionIndex == 1,
        "language switch refreshes action label without changing saved action");
    window.Width = 340; Pump(); Capture("tools-narrow.png");
    Wait(runtime.InvokeAsync("sleep-timer", "开始定时")); Require(timer.State.Active, "settings action reaches shared timer");
    Wait(runtime.InvokeAsync("sleep-timer", "取消定时")); Require(!timer.State.Active, "settings cancellation reaches shared timer");
    Wait(session.SetQueueAsync([a with { Metadata = new Dictionary<string, string?> { ["isLiveStream"] = "true" } }], "a"));
    Until(() => !tools.CanLoop); Require(!panel.FindControl<Button>("StartLoop")!.IsEnabled, "live stream has no segment control");
    Console.WriteLine($"PASS {count} playback-tools behavior checks; screenshots: {root}");
}
finally
{
    window.Content = null; window.Close(); runtime.Dispose(); tools.Dispose();
    Wait(timer.DisposeAsync().AsTask()); Wait(session.DisposeAsync().AsTask());
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
void Until(Func<bool> condition, int milliseconds = 2000)
{
    var clock = Stopwatch.StartNew();
    while (!condition()) { if (clock.ElapsedMilliseconds > milliseconds) throw new TimeoutException("playback-tools condition"); Pump(); Thread.Sleep(10); }
    Pump();
}
void Wait(Task task) { Until(() => task.IsCompleted, 3500); task.GetAwaiter().GetResult(); }
void Click(string name)
{
    Pump(); var button = panel.FindControl<Button>(name)!;
    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
}
void Capture(string name)
{
    for (var frameIndex = 0; frameIndex < 3; frameIndex++) { Pump(); Thread.Sleep(20); }
    using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(root, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
}
void ApplyLanguage() { foreach (var key in text.Catalog.Fallback.Strings.Keys) window.Resources[key] = text[key]; }
void ApplyTheme(bool dark)
{
    window.Resources.ThemeDictionaries[dark ? ThemeVariant.Dark : ThemeVariant.Light] =
        ThemeResourcePalette.Create(dark ? preset.Dark : preset.Light, new Echo.Contracts.Settings.ThemePreferences(), dark, null);
    window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    window.Bind(Window.BackgroundProperty, window.GetResourceObservable("EchoPage"));
}
