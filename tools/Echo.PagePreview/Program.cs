using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Contracts.Scanning;
using Echo.Contracts.Library;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Core.Client;
using Echo.Application.Remote;
using Echo.Infrastructure.Remote;
using Echo.Infrastructure.Podcast;
using Echo.Desktop.Features.Podcasts;
using Echo.Desktop.Features.Connect;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Artists;
using Echo.Desktop.Features.Genres;
using Avalonia.VisualTree;

// Isolated native Avalonia renders. No production database, audio output or Core service.
AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/pages");
Directory.CreateDirectory(output);
var opening = LibraryDatabase.OpenAsync(Path.Combine(output, $"preview-{Guid.NewGuid():N}.sqlite"));
Wait(opening);
var library = opening.GetAwaiter().GetResult();
var focusHome = args.Contains("--home-player", StringComparer.Ordinal);
using var homePlayback = focusHome ? new HomePlayerPlayback() : null;
using var playback = focusHome ? null : new PreviewPlayback();
var runtime = homePlayback?.Runtime ?? playback!.Runtime;
using var bridge = new BridgeClientRuntime(null);
using var remoteFiles = new RemoteFileClients();
using var podcastHandler = new PagePodcastHandler();
using var podcasts = new PodcastLibrary(Path.Combine(output, $"podcasts-{Guid.NewGuid():N}.sqlite"), podcastHandler);
MainWindow? window = null;
try
{
    var media = Path.Combine(output, "media"); Directory.CreateDirectory(media);
    var begin = library.BeginScanAsync(media, false, CancellationToken.None); Wait(begin);
    var entries = Enumerable.Range(0, 8).Select(index =>
    {
        var item = HistoryPreviewFixture.Create(media, index);
        return new ScanEntry(new(item.Path, LibraryPath.Key(item.Path), 44, 123), ScanEntryKind.Metadata,
            new(focusHome && index == 0 ? "在城市入睡之后，我们沿着漫长的海岸线继续远行 · A Very Long Midnight Journey" : item.Title,
                item.Artist, item.Album, item.Artist, 240, "wav", 96000, 24, index + 1, 1, 2026, Genre: index % 2 == 0 ? "Ambient" : "Jazz"));
    }).ToArray();
    Wait(library.CommitScanBatchAsync(begin.Result, 1, entries, CancellationToken.None));
    Wait(library.FinishScanAsync(begin.Result, ScanStatus.Completed, 8, false, null, CancellationToken.None));
    var tracks = library.GetTracksAsync(new(Limit: 8)); Wait(tracks);
    var playlist = library.CreatePlaylistAsync("preview-create", "夜航 · After Hours", "", CancellationToken.None); Wait(playlist);
    Wait(library.AddPlaylistTracksAsync("preview-add", playlist.Result, tracks.Result.Items.Select(track => track.Id).ToArray()));
    foreach (var track in tracks.Result.Items)
    {
        var started = DateTimeOffset.Now.AddHours(-1).ToUnixTimeSeconds();
        Wait(library.RecordListeningAsync(new("preview-listen-" + track.Id, track.Id, track.Path, started, started + 180, 180)));
    }
    window = DesktopComposition.CreateWindow(false, false, library, playback: runtime);
    window.Width = 1440; window.Height = 960;
    var shell = (ShellViewModel)window.DataContext!;
    shell.AttachConnect(bridge);
    shell.AttachRemote(new RemoteSourceCatalog(null, remoteFiles), remoteFiles);
    shell.AttachPodcasts(podcasts, runtime.LocalZone.Session);
    shell.AlbumInformation = null; // Capture local metadata without online enrichment.
    shell.Text.SetLanguage("zh-CN");
    shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
    window.Show();
    if (homePlayback is not null)
    {
        HomePlayerCapture.Run(window, shell, tracks.Result.Items, homePlayback, output);
        return;
    }
    string[] routes = ["home", "songs", "albums", "artists", "album-artists", "genres", "folders", "playlists", "history", "queue", "search", "radio", "podcasts", "remote", "connect", "dsp", "settings", "lyrics", "workshop", "listening"];
    foreach (var route in routes)
    {
        shell.NavigateCommand.Execute(route);
        Pump(250);
        foreach (var dark in new[] { false, true })
        {
            shell.IsDark = dark; Pump(60);
            Save($"{route}-{(dark ? "dark" : "light")}.png");
        }
        window.Width = 1000; Pump(60); Save(route + "-compact.png");
        window.Width = 1440;
        Console.WriteLine($"Rendered {route}");
    }
    shell.NavigateCommand.Execute("podcasts"); Pump(100);
    var podcastModel = (PodcastViewModel)window.GetVisualDescendants().OfType<PodcastView>().Single().DataContext!;
    podcastModel.ToggleEditorCommand.Execute(null); Pump(40);
    if (!podcastModel.ShowSubscriptionRail || podcastModel.ShowWelcome) throw new InvalidOperationException("Podcast editor is hidden.");
    Save("podcasts-editor.png");
    podcastModel.ToggleEditorCommand.Execute(null);
    if (!podcastModel.ShowWelcome) throw new InvalidOperationException("Podcast welcome did not return.");
    Wait(podcasts.SubscribeAsync("https://example.com/echo-preview.xml", CancellationToken.None));
    shell.NavigateCommand.Execute("home"); shell.NavigateCommand.Execute("podcasts"); Pump(180);
    podcastModel = (PodcastViewModel)window.GetVisualDescendants().OfType<PodcastView>().Single().DataContext!;
    CaptureVariants("podcasts-populated");
    podcastModel.ShowQuery = "no matching show"; Pump(40);
    if (!podcastModel.ShowSubscriptionRail || podcastModel.ShowWelcome) throw new InvalidOperationException("Podcast search hid its navigation.");
    Save("podcasts-no-results.png");
    shell.NavigateCommand.Execute("connect"); Pump(60);
    var connect = ((ConnectView)window.GetVisualDescendants().OfType<ConnectView>().Single()).Model;
    foreach (var item in connect.Items)
    {
        connect.Select(item.Mode.Key); Pump(40); CaptureVariants("connect-" + item.Mode.Key);
    }
    shell.NavigateCommand.Execute("settings");
    foreach (var category in shell.Settings.NavigationGroups.SelectMany(group => group.Items).ToArray())
    {
        shell.Settings.SelectCategoryCommand.Execute(category.Id); Pump(50);
        CaptureVariants("settings-" + category.Id);
    }
    shell.NavigateCommand.Execute("albums"); Pump(100);
    var albums = window.GetVisualDescendants().OfType<AlbumsView>().Single();
    if (albums.Model.At(0) is { } album) { albums.OpenDetail(album); Pump(120); CaptureVariants("album-detail"); }
    shell.NavigateCommand.Execute("artists"); Pump(100);
    var artists = window.GetVisualDescendants().OfType<ArtistsView>().Single();
    if (artists.Model.At(0) is { } artist) { artists.OpenDetail(artist); Pump(120); CaptureVariants("artist-detail"); }
    shell.NavigateCommand.Execute("genres"); Pump(100);
    var genres = window.GetVisualDescendants().OfType<GenresView>().Single();
    if (genres.Model.At(0) is { } genre) { genres.OpenDetail(genre); Pump(120); CaptureVariants("genre-detail"); }
    Console.WriteLine("Rendered details, settings categories, Connect workspaces and podcast state transitions.");
}
finally
{
    window?.RequestExit(); Pump(30);
    Wait(library.DisposeAsync().AsTask());
}

void Save(string name)
{
    using var frame = window!.CaptureRenderedFrame() ?? throw new IOException("Avalonia returned no frame.");
    frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
}
void CaptureVariants(string name)
{
    window!.Width = 1440; shellTheme(false); Save(name + "-light.png");
    shellTheme(true); Save(name + "-dark.png");
    window.Width = 1000; Pump(40); Save(name + "-compact.png");
    window.Width = 1440;
    void shellTheme(bool dark) { ((ShellViewModel)window.DataContext!).IsDark = dark; Pump(40); }
}
static void Pump(int milliseconds)
{
    var clock = Stopwatch.StartNew();
    do
    {
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); Thread.Sleep(5);
    } while (clock.ElapsedMilliseconds < milliseconds);
}
static void Wait(Task task)
{
    var clock = Stopwatch.StartNew();
    while (!task.IsCompleted)
    {
        if (clock.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Page preview fixture");
        Pump(5);
    }
    task.GetAwaiter().GetResult();
}
