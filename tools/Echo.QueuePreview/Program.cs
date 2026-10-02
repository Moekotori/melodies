using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Queue;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Playback;

// Fresh production queue controls, isolated from unfinished parallel pages. Audio is simulated.
Console.WriteLine("capture:setup");
AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions()).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
Console.WriteLine("capture:styles");
Application.Current!.Styles.Add(new StyleInclude(new Uri("avares://Echo.QueuePreview/")) { Source = new Uri("avares://Echo.QueuePreview/Queue/QueueStyles.axaml") });
var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
using var data = JsonDocument.Parse(File.ReadAllText(args[1]));
var tracks = data.RootElement.EnumerateArray().Take(20).Select((entry, i) =>
{
    var track = entry.GetProperty("track");
    string Value(string key) => track.TryGetProperty(key, out var field) && field.ValueKind != JsonValueKind.Null ? field.ToString() : "";
    return new QueueTrack($"ref-{i}", Value("id"), Value("path"), new("http", "http://127.0.0.1/preview-only"),
        new Dictionary<string, string?> { ["title"] = Value("title"), ["artist"] = Value("artist"), ["album"] = Value("album"), ["duration"] = Value("duration"), ["codec"] = Value("codec"), ["bitDepth"] = Value("bitDepth"), ["sampleRate"] = Value("sampleRate") }, Origin: new("manual", "手动添加"));
}).ToArray();
var native = new PreviewNative(double.Parse(tracks[0].Metadata!["duration"]!, System.Globalization.CultureInfo.InvariantCulture));
var session = new PlaybackSessionCoordinator(native);
Console.WriteLine("capture:shell");
var shell = new ShellViewModel();
var player = new PlayerViewModel(new PlaybackZone("local", "本机", session), shell.Text);
shell.AttachPlayback(player);
var artwork = new AlbumArtworkCache(new LocalAlbumArtwork()); player.Queue.Artwork = artwork;
shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == MotionMode.Off);
Console.WriteLine("capture:window");
var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
var watch = System.Diagnostics.Stopwatch.StartNew();
try
{
    // The native queue is the only replaced surface; chrome comes from the last coherent build.
    foreach (var key in shell.Text.Catalog.Fallback.Strings.Keys) window.Resources[key] = shell.Text[key];
    Console.WriteLine("capture:show");
    window.Show(); Pump(session.SetQueueAsync(tracks, tracks[0].ItemId)); Pump(session.PauseAsync());
    shell.NavigateCommand.Execute("queue");
    window.FindControl<MotionPresenter>("PageHost")!.Content = new QueueView(player, shell.Text, false) { Margin = new Thickness(42,24,42,28) };
    foreach (var dark in new[] { false, true }) { shell.IsDark = dark; Settle(); Save(dark ? "queue-reference-dark.png" : "queue-reference-light.png"); }
    window.Width = 900; window.Height = 720; Settle(); Save("queue-reference-compact.png");
    Console.WriteLine("PASS current queue sources compiled/rendered in last built shell; visual preview only, simulated audio.");
}
finally { player.Detach(); window.Close(); shell.Settings.Dispose(); Pump(session.DisposeAsync().AsTask()); Pump(artwork.DisposeAsync().AsTask()); }
void Pump(Task task)
{
    while (!task.IsCompleted) { if (watch.Elapsed.TotalSeconds > 12) throw new TimeoutException("Queue visual capture"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
    task.GetAwaiter().GetResult(); Settle();
}
void Settle() { for (var i = 0; i < 8; ++i) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); } AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
void Save(string name) { using var shot = window.CaptureRenderedFrame() ?? throw new IOException("Queue frame missing"); shot.Save(Path.Combine(output,name), PngBitmapEncoderOptions.Default); Console.WriteLine(Path.Combine(output,name)); }
sealed class PreviewNative(double duration) : INativeAudio
{
    private int operation;
    public event Action<string, JsonElement>? Notification { add { } remove { } }
    public event Action<Exception>? Failed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        var json = method switch { "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}", "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}", "audio.openFile" or "audio.openSource" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":{duration.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}", _ => "true" };
        using var doc = JsonDocument.Parse(json); return Task.FromResult(doc.RootElement.Clone());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
