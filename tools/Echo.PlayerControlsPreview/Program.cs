using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Shell;
using Echo.Playback;
using Echo.Application.Playback;
using Echo.Desktop.Features.Settings.Runtime;
using Echo.Playback.NativeAudio;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var root = Path.GetFullPath(args.FirstOrDefault() ?? "misc/player-controls/previews");
Directory.CreateDirectory(root);
var clock = Stopwatch.StartNew();
var native = new ControlNative();
var session = new PlaybackSessionCoordinator(native);
var timer = new SleepTimerService(session);
var shell = new ShellViewModel(); shell.Text.SetLanguage("zh-CN");
var player = new PlayerViewModel(new("local", "本机", session), shell.Text); shell.AttachPlayback(player);
player.AttachTools(timer, new FeaturePreferences());
var panel = new PlayerVolumePanel { DataContext = shell };
var surface = new Border { Padding = new Thickness(10, 8, 10, 14), CornerRadius = new CornerRadius(16), Child = panel };
surface.Bind(Border.BackgroundProperty, surface.GetResourceObservable("EchoPlayer"));
var window = new Window { Width = 286, Height = 230, Content = surface, DataContext = shell };
ApplyLanguage();
try
{
    window.Show(); Pump();
    Wait(player.ChangeVolumeAsync(.23)); Wait(player.ChangePlaybackRateAsync(1.25));
    Until(() => player.PlaybackRate == session.Snapshot.PlaybackRate && player.Volume == .23);
    Capture("controls-light.png");
    Click("SpeedUpButton"); Until(() => Math.Abs(session.Snapshot.PlaybackRate - 1.30) < .001);
    Require(session.Snapshot.PlaybackRate == player.PlaybackRate, "popup displays confirmed session rate");
    var speed = panel.FindControl<Slider>("SpeedSlider")!;
    var start = speed.TranslatePoint(new Point(speed.Bounds.Width * .45, speed.Bounds.Height / 2), window)!.Value;
    var end = speed.TranslatePoint(new Point(speed.Bounds.Width * .7, speed.Bounds.Height / 2), window)!.Value;
    var calls = native.RateCalls;
    window.MouseDown(start, MouseButton.Left); window.MouseMove(end); Pump();
    Require(player.IsChangingPlaybackRate && native.RateCalls == calls, "drag only previews; no command flood");
    window.MouseUp(end, MouseButton.Left); Until(() => native.RateCalls == calls + 1 && !player.IsChangingPlaybackRate && !player.IsBusy);
    Require(Math.Abs(speed.Value - session.Snapshot.PlaybackRate) < .001, "slider returns confirmed receipt");
    speed.Focus(); window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
    window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
    Until(() => native.RateCalls == calls + 2 && !player.IsChangingPlaybackRate && !player.IsBusy);
    native.RejectRate = true; var previous = session.Snapshot.PlaybackRate;
    Click("SpeedDownButton"); Until(() => !player.IsBusy && player.Error.Length > 0);
    Require(session.Snapshot.PlaybackRate == previous && player.PlaybackRate == previous, "native failure preserves confirmed rate");
    native.RejectRate = false; Click("ResetSpeedButton"); Until(() => session.Snapshot.PlaybackRate == 1 && !player.IsBusy);
    Click("VolumeUpButton"); Until(() => player.Volume == .28 && !player.IsBusy);
    Click("MuteButton"); Until(() => player.Volume == 0 && !player.IsBusy);
    Click("MuteButton"); Until(() => player.Volume == .28 && !player.IsBusy);
    Click("VolumeLockButton");
    Require(!panel.FindControl<Grid>("VolumeAdjustment")!.IsEnabled && !panel.FindControl<Button>("MuteButton")!.IsEnabled, "volume lock prevents panel adjustments");
    Click("VolumeLockButton");
    Wait(session.SetPlaybackRateAsync(1.5)); Until(() => player.PlaybackRate == 1.5);
    var checkpoint = session.ExportCheckpointAsync(); Wait(checkpoint);
    var restored = new PlaybackSessionCoordinator(new ControlNative());
    try
    {
        Wait(restored.RestoreCheckpointAsync(checkpoint.Result));
        Require(restored.Snapshot.PlaybackRate == 1.5, "checkpoint retains rate");
        Wait(restored.RecoverAsync()); Require(restored.Snapshot.PlaybackRate == 1.5, "recovery confirms rate again");
    }
    finally { Wait(restored.DisposeAsync().AsTask()); }
    window.RequestedThemeVariant = ThemeVariant.Dark; Pump(); Capture("controls-dark.png");
    shell.Text.SetLanguage("en-US"); ApplyLanguage(); Pump(); Capture("controls-english.png");
    window.RequestedThemeVariant = ThemeVariant.Light; shell.Text.SetLanguage("zh-CN"); ApplyLanguage();
    Click("SleepTab");
    var sleepPanel = (PlayerSleepTimerPanel)panel.FindControl<ContentControl>("ExtraPane")!.Content!;
    Require(sleepPanel.DataContext == player.Tools && !panel.FindControl<StackPanel>("VolumePane")!.IsVisible, "sleep tab binds shared timer without navigating away");
    Wait(player.Tools!.StartTimerCommand.ExecuteAsync(null));
    Require(timer.State.Active, "embedded sleep timer starts shared deadline");
    window.Height = 280; Pump(); Capture("controls-sleep.png");
    Click("MiniTab"); Require(panel.FindControl<ContentControl>("ExtraPane")!.Content is PlayerMiniPanel && timer.State.Active, "switching tabs preserves background timer");
    Capture("controls-mini.png");
    window.Height = 340; Click("OutputTab"); Require(panel.FindControl<ContentControl>("ExtraPane")!.Content is PlayerQuickOutputPanel, "output tab stays in the same popover");
    Capture("controls-output.png");
    Click("VolumeTab"); window.Height = 230;
    panel.FindControl<Button>("VolumeTab")!.Focus();
    window.KeyPress(Key.Q, RawInputModifiers.None, PhysicalKey.Q, null); window.KeyRelease(Key.Q, RawInputModifiers.None, PhysicalKey.Q, null);
    Require(speed.IsFocused, "Q focuses speed control");
    window.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, null); window.KeyRelease(Key.E, RawInputModifiers.None, PhysicalKey.E, null);
    Require(panel.FindControl<Slider>("VolumeSlider")!.IsFocused, "E focuses volume control");
    Wait(player.Tools.CancelTimerCommand.ExecuteAsync(null));
    window.Content = null; Pump(); Require(!player.IsChangingPlaybackRate && !player.IsChangingVolume, "detach releases preview ownership");
    var bar = new PlayerBar { DataContext = shell };
    window.Width = 1100; window.Height = 480;
    window.Content = new StackPanel { Children = { new Border { Height = 330 }, bar } }; Pump();
    var volumeButton = bar.FindControl<Button>("VolumeButton")!;
    var point = volumeButton.TranslatePoint(new Point(volumeButton.Bounds.Width / 2, volumeButton.Bounds.Height / 2), window)!.Value;
    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    var flyout = (Flyout)volumeButton.Flyout!;
    Require(flyout.IsOpen, "bottom-right button opens actual product flyout");
    var popupPanel = ((Control)flyout.Content!).GetVisualDescendants().OfType<PlayerVolumePanel>().Single();
    Require(popupPanel.DataContext == shell, "actual flyout binds shared shell");
    var popupSpeed = popupPanel.FindControl<Slider>("SpeedSlider")!;
    popupSpeed.Focus(); window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null); Pump();
    Require(player.IsChangingPlaybackRate, "popup starts keyboard preview");
    flyout.Hide(); Pump(); Require(!player.IsChangingPlaybackRate, "closing popup cancels uncommitted preview");
    for (var i = 0; i < 5; i++) { flyout.ShowAt(volumeButton); Pump(); flyout.Hide(); Pump(); }
    window.Content = null; Pump();
    if (args.Length > 1)
    {
        var real = new PlaybackSessionCoordinator(new NativeAudioHost(Path.GetFullPath(args[1]), allowProDsp: true));
        try
        {
            Wait(real.SetVolumeAsync(0)); Wait(real.SetPlaybackRateAsync(.75));
            Require(Math.Abs(real.Snapshot.PlaybackRate - .75) < .001, "real native .75x receipt");
            Wait(real.SetPlaybackRateAsync(1.25));
            Require(Math.Abs(real.Snapshot.PlaybackRate - 1.25) < .001, "real native 1.25x receipt");
            Wait(real.SetPlaybackRateAsync(1));
        }
        finally { Wait(real.DisposeAsync().AsTask()); }
        Console.WriteLine("PASS real Native Audio Host speed receipts (.75x, 1.25x, reset), silent volume. No acoustic/device acceptance claimed.");
    }
    Console.WriteLine($"PASS product Avalonia XAML: drag/keyboard, one command per drag, failure rollback, shared speed, +/- volume, mute restore, lock, four panes, shared sleep timer, Q/E focus, checkpoint/recovery, detach, light/dark/English. {clock.Elapsed.TotalSeconds:0.0}s.");
}
finally { window.Close(); player.Detach(); Wait(timer.DisposeAsync().AsTask()); Wait(session.DisposeAsync().AsTask()); }

void ApplyLanguage()
{ foreach (var key in shell.Text.Catalog.Fallback.Strings.Keys) window.Resources[key] = shell.Text[key]; }
void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
void Until(Func<bool> condition)
{ while (!condition()) { if (clock.Elapsed.TotalSeconds > 22) throw new TimeoutException("Player controls preview"); Pump(); Thread.Sleep(2); } Pump(); }
void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
void Click(string name)
{
    var button = panel.FindControl<Button>(name)!;
    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
}
void Capture(string name)
{ Pump(); using var image = window.CaptureRenderedFrame() ?? throw new IOException("Render unavailable"); image.Save(Path.Combine(root, name), PngBitmapEncoderOptions.Default); }

sealed class ControlNative : INativeAudio
{
    public bool RejectRate { get; set; }
    public int RateCalls { get; private set; }
    public event Action<string, JsonElement>? Notification { add { } remove { } }
    public event Action<Exception>? Failed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        if (method == "playbackRate.configure")
        { RateCalls++; if (RejectRate) throw new IOException("fixture rate rejected"); return Task.FromResult(parameters!.Value[0].Clone()); }
        var json = method == "queue.set" ? "{\"queueRevision\":" + parameters!.Value.GetProperty("revision").GetInt64() + "}" : "true";
        using var doc = JsonDocument.Parse(json); return Task.FromResult(doc.RootElement.Clone());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
