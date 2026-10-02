using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Localization;
using Echo.Playback;

// Uses product XAML and owner state; native replies are fixtures, no device or production database.
AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var root = Path.GetFullPath(args.FirstOrDefault() ?? "misc/signal-path/previews");
Directory.CreateDirectory(root);
var clock = Stopwatch.StartNew();
var file = Path.Combine(root, "source.flac"); File.WriteAllBytes(file, []);
var native = new SignalNative();
var session = new PlaybackSessionCoordinator(native);
var text = new LocalizationService(); text.SetLanguage("zh-CN");
var player = new PlayerViewModel(new("local", "本机", session), text);
var button = new Button { Content = "Signal path" };
var popup = SignalPathPanel.Create(button, () => player, () => { }); button.Flyout = popup;
var window = new Window { Width = 1000, Height = 1000, Content = button };
SignalPathViewModel? view = null;
try
{
    window.Show(); Pump();
    using var filtered = session.Subscribe(PlaybackStateChanges.Queue | PlaybackStateChanges.Clock);
    Wait(filtered.ReadAsync());
    Wait(session.SetQueueAsync([new("a", "a", file, Metadata: new Dictionary<string, string?> { ["codec"] = "flac", ["sampleRate"] = "96000" })], "a"));
    native.Emit("audio.started", "{\"operationId\":1}");
    Until(() => session.Snapshot.Status == PlaybackStatus.Playing);
    player.Details.BitDepth = 24; player.Details.SampleRate = 96000;
    popup.ShowAt(button); Pump();
    var panel = (SignalPathPanel)popup.Content!;
    view = (SignalPathViewModel)panel.DataContext!;
    Require(view.Spec.Contains("96") && view.Headline == "原生播放", "source and direct summary");
    Require(view.OutputFacts.Single(x => x.Label == "实际输出采样率").Value == "96 kHz", "confirmed hardware rate");
    Require(view.LevelText == "--", "unknown meter must not fabricate readings");
    Require(panel.Bounds.Width <= 400 && panel.Bounds.Height <= 540 && double.IsNaN(panel.Height), "compact content-sized panel");
    Require(!panel.FindControl<Expander>("DiagnosticsExpander")!.IsExpanded, "details folded by default");
    Capture(panel, "signal-light.png");
    Wait(filtered.ReadAsync());
    for (var i = 0; i < 256; i++) native.Emit("audio.levelMeter", "{\"operationId\":1,\"peakDb\":[-12,-6],\"rmsDb\":[-20,-18]}");
    Until(() => session.State.Level?.PeakDb == -6);
    Until(() => view.LevelText.Contains("-6.0"));
    Require(view.LevelPercent == 80, "peak aggregation and meter tick mapping");
    using (var noTick = new CancellationTokenSource(35))
    {
        var read = filtered.ReadAsync(noTick.Token);
        Until(() => read.IsCompleted);
        Require(read.IsCanceled, "meter must not wake queue/clock consumers");
    }
    Capture(panel, "signal-live.png");
    player.Details.SampleRate = 192000;
    Until(() => view.Headline == "格式转换");
    Require(view.Nodes.Count == 4, "conversion stage visible in compact chain");
    Require(view.Metrics[1].Highlight && view.Metrics[3].Highlight && view.Metrics[3].Value == "192 kHz → 96 kHz", "changed processing and clocks highlighted");
    Capture(panel, "signal-converted.png");
    player.Details.SampleRate = 96000;
    Until(() => view.Headline == "原生播放");
    Require(!view.Metrics[1].Highlight && !view.Metrics[3].Highlight, "unchanged route is neutral");
    native.Emit("audio.levelMeter", "{\"operationId\":999,\"peakDb\":[0]}");
    Wait(session.SetVolumeAsync(1));
    Require(session.State.Level?.PeakDb == -6, "stale operation rejected");
    native.Emit("audio.levelMeter", "{\"operationId\":1,\"peakDb\":[\"bad\",null]}");
    Wait(session.SetVolumeAsync(1));
    Require(session.State.Level?.PeakDb == -6, "invalid values rejected");
    Wait(session.PauseAsync()); Until(() => view.LevelText == "--");
    Require(session.State.Level is null, "pause clears owner meter");
    window.RequestedThemeVariant = ThemeVariant.Dark; Pump(); Capture(panel, "signal-dark.png");
    text.SetLanguage("en-US"); Pump(); Capture(panel, "signal-english.png");
    panel.Width = 320; panel.MaxHeight = 500; Pump(); Capture(panel, "signal-narrow.png");
    panel.FindControl<Expander>("DiagnosticsExpander")!.IsExpanded = true;
    var scroll = panel.FindControl<ScrollViewer>("ContentScroll")!;
    Until(() => scroll.Extent.Height > 700);
    scroll.Offset = new Vector(0, 1500);
    Until(() => scroll.Offset.Y > 100);
    Capture(panel, "signal-diagnostics.png");
    popup.Hide(); Pump(); Require(panel.DataContext is null, "closing releases panel state");
    // Repeated opens must release subscription admission, not consume the 32 slots.
    for (var i = 0; i < 40; i++) { popup.ShowAt(button); Pump(); Require(!panel.FindControl<Expander>("DiagnosticsExpander")!.IsExpanded, "reopen folds details"); popup.Hide(); Pump(); }
    Wait(session.StopAsync());
    Console.WriteLine($"PASS native product XAML, unknown/live/paused readings, burst coalescing, stale/invalid frames, category isolation, reopen disposal, light/dark/English/narrow. {clock.Elapsed.TotalSeconds:0.0}s. Audio simulated.");
}
finally
{
    popup.Hide(); view?.Dispose(); player.Detach(); window.Close(); Wait(session.DisposeAsync().AsTask());
}

void Pump()
{
    Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
}
void Until(Func<bool> predicate)
{ while (!predicate()) { if (clock.Elapsed.TotalSeconds > 18) throw new TimeoutException("Signal preview"); Pump(); Thread.Sleep(2); } Pump(); }
void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
void Capture(Control panel, string name)
{
    Pump(); panel.UpdateLayout();
    using var image = new RenderTargetBitmap(new PixelSize((int)(panel.Bounds.Width * 1.25), (int)(panel.Bounds.Height * 1.25)), new Vector(120, 120));
    image.Render(panel); image.Save(Path.Combine(root, name), PngBitmapEncoderOptions.Default);
}

sealed class SignalNative : INativeAudio
{
    public event Action<string, JsonElement>? Notification;
    public event Action<Exception>? Failed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Emit(string method, string json)
    { using var doc = JsonDocument.Parse(json); Notification?.Invoke(method, doc.RootElement.Clone()); }
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        var json = method switch
        {
            "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
            "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":96000,\"hardwareSampleRate\":96000,\"channels\":2,\"exclusive\":false,\"dspActive\":false,\"format\":\"pcm\",\"backend\":\"wasapi\",\"deviceName\":\"数字音频接口 (TEAC USB AUDIO DEVICE (Isoch))\",\"actualBufferFrames\":1024}}",
            "audio.openFile" => "{\"operationId\":1,\"sampleRate\":96000,\"durationSeconds\":223}",
            _ => "true"
        };
        using var doc = JsonDocument.Parse(json); return Task.FromResult(doc.RootElement.Clone());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
