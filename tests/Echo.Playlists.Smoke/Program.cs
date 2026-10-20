using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using Echo.Application.Playback;
using Echo.Core;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Playlists;
using Echo.Desktop.Shell;
using Echo.Contracts.Library;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = true }).SetupWithoutStarting();
var uiOnly = args.Length > 1 && args[0] == "--ui-only";
var root = Path.GetFullPath(uiOnly ? args[1] : Path.Combine("misc", "playlist-parity", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
if (!uiOnly) Pump(Task.Run(() => PlaylistCoreChecks.RunAsync(root, budget.Token)));
if (args.Contains("--core-only")) { Console.WriteLine("ARTIFACTS " + root); return; }

var runtime = new PlaybackRuntime(new FixtureAudio());
var core = new CoreRuntime(runtime, Path.Combine(root, "library.sqlite"));
var artwork = new AlbumArtworkCache(new EmptyArtwork());
var player = new PlayerViewModel(runtime.LocalZone);
var shell = new ShellViewModel(); shell.Text.SetLanguage("zh-CN");
shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == Echo.Desktop.Motion.MotionMode.Off);
shell.AttachLibrary(core.Library, artwork); shell.AttachPlayback(player);
var window = new MainWindow { DataContext = shell, Width = 1100, Height = 740 };
PlaylistsView? view = null;
try
{
    window.Show(); shell.NavigateCommand.Execute("playlists");
    Wait(() => window.GetVisualDescendants().OfType<PlaylistsView>().Any());
    view = window.GetVisualDescendants().OfType<PlaylistsView>().Single();
    Wait(() => !view.Model.IsBrowsing && view.Model.Playlists.Count == 2);
    var playlist = view.Model.Playlists.Single(row => row.Name == "Collection");
    view.Model.OpenPlaylist(playlist); Wait(() => !view.Model.IsLoading && view.Model.Tracks.Count > 0);
    Check(view.Model.PlayLabel == shell.Text["playlists.play"], "cross-page action label");
    Check(view.FindControl<Button>("ExportPlaylistButton")!.IsEnabled, "export is connected to Core capability");
    Click(view.FindControl<Button>("PlayButton")!); Wait(() => !view.Model.IsActing);
    Check(runtime.LocalZone.Session.Queue.Count == 246, "real play button spans all playlist pages");
    byte[]? exported = null;
    Pump(view.Model.ExportFileAsync("m3u8", (content, _) => { exported = content; return Task.CompletedTask; }));
    Check(exported is { Length: > 0 } && view.Model.Error.Length == 0, "export action produces contents");
    Pump(core.Library.ArchivePlaylistAsync(Guid.NewGuid().ToString("N"), playlist.Id, true, budget.Token));
    view.Model.Open("home"); Pump(view.Model.LoadBrowserAsync());
    Click(view.FindControl<Button>("PlaylistFilesButton")!);
    Click(view.FindControl<Button>("RecoverPlaylistsButton")!);
    Wait(() => view.GetVisualDescendants().OfType<PlaylistRecoveryView>().Any(panel => panel.DataContext is PlaylistRecoveryViewModel { IsBusy: false, Total: 1 }));
    Capture("recovery-light.png");
    shell.Theme.Mode = "深色"; Capture("recovery-dark.png");
    shell.Text.SetLanguage("en-US"); window.Width = 680; Capture("recovery-narrow-en.png");
    var recovery = view.GetVisualDescendants().OfType<PlaylistRecoveryView>().Single();
    Check(((PlaylistRecoveryViewModel)recovery.DataContext!).EmptyLabel == shell.Text["playlists.recoverHint"], "recovery text follows live language changes");
    var restore = recovery.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == shell.Text["playlists.restore"]);
    Click(restore); Wait(() => recovery.DataContext is PlaylistRecoveryViewModel { IsBusy: false, Total: 0 });
    var restoredTracks = core.Library.GetPlaylistTracksAsync(new(playlist.Id), budget.Token); Pump(restoredTracks);
    Check(restoredTracks.Result.Total == 246, "restore button retains playlist contents");
    Capture("recovery-empty.png");
    Console.WriteLine("PASS Avalonia: real playback/restore buttons, Core export action, light/dark/narrow English recovery rendering and release");
    Console.WriteLine("ARTIFACTS " + root);
}
finally
{
    window.Close(); view?.Dispose(); player.Detach(); Pump(artwork.DisposeAsync().AsTask()); Pump(core.DisposeAsync().AsTask());
}

void Capture(string name)
{
    Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    using var bitmap = window.CaptureRenderedFrame() ?? throw new Exception("No Avalonia frame");
    bitmap.Save(Path.Combine(root, name), PngBitmapEncoderOptions.Default);
}
void Wait(Func<bool> condition)
{
    while (!condition()) { budget.Token.ThrowIfCancellationRequested(); Settle(); Thread.Sleep(5); }
    Settle();
}
void Pump(Task task)
{ Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
void Click(Button button)
{
    Settle();
    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window) ?? throw new Exception("Button is outside the window");
    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle();
}
static void Check(bool value, string message) { if (!value) throw new Exception(message); }

sealed class EmptyArtwork : IAlbumArtwork
{ public Task<byte[]?> ReadAsync(string samplePath, CancellationToken cancellation) => Task.FromResult<byte[]?>(null); }
