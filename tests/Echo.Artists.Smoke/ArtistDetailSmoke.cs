using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Artists;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playback;

internal static class ArtistDetailSmoke
{
    public static void Run(string root)
    {
        var database = LibraryDatabase.OpenAsync(Path.Combine(root, "artists.sqlite")).GetAwaiter().GetResult();
        Seed(database, root);
        var fixture = new LibraryFixture(database);
        var session = new PlaybackSessionCoordinator(new AudioFixture());
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session));
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork());
        var shell = new ShellViewModel(); shell.AttachPlayback(player); shell.AttachLibrary(fixture, artwork);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 1000 };
        try
        {
            window.Show(); shell.NavigateCommand.Execute("artists");
            Until(() => window.GetVisualDescendants().OfType<ArtistsView>().Any());
            var page = window.GetVisualDescendants().OfType<ArtistsView>().Single();
            var query = database.GetArtistsAsync(new(Search: "Aurora")); Wait(query); var artist = query.Result.Items.Single();
            page.OpenDetail(artist);
            Settle();
            var detail = page.GetVisualDescendants().OfType<ArtistDetailView>().Single();
            var model = (ArtistDetailViewModel)detail.DataContext!;
            Until(() => !model.IsLoading && model.Tracks.Count == 200 && model.Overview.Insights is not null);
            Check(model.CanPlay && model.Overview.IsOnlineLoading, "Local browsing and playback remain available while online metadata waits.");
            var stats = model.Overview.Insights!;
            Check(stats is { FavoriteTracks: 1, Plays: 2, UnplayedAlbums: 2, FirstYear: 2018, LastYear: 2024 } && stats.ListenedSeconds == 180,
                "Statistics use all artist tracks and counted history, not the first page or skipped plays.");
            Check(stats.RelatedArtists.Count == 1 && stats.RelatedArtists[0].Artist.Name == "Björk" && stats.Collaborations.Count == 6, "Credits drive distinct collaborator and collaboration projections.");
            Check(stats.TopTracks.Count == 1 && stats.TopTracks[0].Plays == 2 && stats.DiscoverAlbums.Count == 2, "Top tracks and unplayed albums use exact library relationships.");
            fixture.Online.TrySetResult(Information());
            Until(() => !model.Overview.IsOnlineLoading && model.Overview.HasBiography);
            Check(model.VisibleTracks.Count == 6, "The overview shows six track previews.");
            Wait(model.Tracks[0].ToggleFavoriteCommand.ExecuteAsync(null));
            Check(model.Overview.Insights!.FavoriteTracks == 0, "Successful favorite changes immediately update artist facts.");
            Wait(model.Tracks[0].ToggleFavoriteCommand.ExecuteAsync(null));
            shell.Text.SetLanguage("en-US");
            Check(model.Meta.Contains("tracks") && model.Overview.BiographyAction == "Expand biography" && model.Overview.Facts.Any(fact => fact.Label == "Area"), "Artist copy, formats and profile fields follow the selected language.");
            Capture("artist-english-light.png"); shell.Text.SetLanguage("zh-CN");
            Capture("artist-overview-light.png"); shell.IsDark = true; Capture("artist-overview-dark.png");
            var scroll = detail.FindControl<ScrollViewer>("PageScroll")!;
            Check(Math.Abs(scroll.TranslatePoint(new Point(scroll.Bounds.Width, 0), window)!.Value.X - window.ClientSize.Width) < 2, "Artist scrolling reaches the window edge.");
            scroll.Offset = new(0, 540); Capture("artist-information-dark.png");
            window.Width = 800; Capture("artist-narrow-dark.png");
            shell.IsDark = false; Capture("artist-narrow-light.png");
            Check(detail.FindControl<ArtistOverviewView>("OverviewPanel")!.FindControl<Avalonia.Controls.Primitives.UniformGrid>("ListeningFacts")!.Columns == 2, "Narrow statistics wrap without losing facts.");
            window.Width = 1440; scroll.Offset = default;
            Click(detail.GetVisualDescendants().OfType<Button>().First(button => button.Tag?.ToString() == "2"));
            Check(model.IsTracks && model.VisibleTracks.Count == 200 && detail.GetVisualDescendants().OfType<ListBoxItem>().Count() < 30, "Full track pages remain virtualized.");
            Wait(model.NextCommand.ExecuteAsync(null)); Check(model.Tracks.Count == 50, "Artist paging reaches tracks outside the first page.");
            Wait(model.PreviousCommand.ExecuteAsync(null)); Wait(model.ShuffleCommand.ExecuteAsync(null));
            Check(session.Queue.Count == 250, $"Artist shuffle uses the complete bounded collection: queue={session.Queue.Count}, error={model.Error}.");
            Capture("artist-tracks-light.png");
            Click(detail.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("artist-tab") && button.Tag?.ToString() == "1"));
            Until(() => detail.GetVisualDescendants().OfType<AlbumWallViewport>().Any(wall => wall.RealizedCards > 0));
            Check(detail.GetVisualDescendants().OfType<AlbumWallViewport>().Single().RealizedCards <= 48, "Album realization remains bounded.");
            Capture("artist-albums-light.png");
            var copiedArtist = stats.RelatedArtists[0].Artist;
            var opened = false; detail.OpenArtist += target => opened = target.Id == copiedArtist.Id;
            Click(detail.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("artist-tab") && button.Tag?.ToString() == "0"));
            Click(detail.GetVisualDescendants().OfType<Button>().First(button => button.Tag is ArtistRow));
            Check(opened, "Collaborator navigation exposes the authoritative artist reference.");
            shell.NavigateCommand.Execute("home"); Settle();
            Check(model.Tracks.Count == 0 && model.Overview.Insights is null && model.Overview.Information is null, "Navigation releases artist pages and article data.");
            Console.WriteLine("PASS artist detail: actual SQLite aggregates, counted history, bounded credits, async metadata, pagination, virtualized rows, queue commands, album wall, related navigation, light/dark/narrow Avalonia rendering and release. Audio uses a protocol fixture.");
        }
        finally
        {
            window.ReleaseLibraryPage(); window.Close(); player.Detach();
            Wait(session.DisposeAsync().AsTask()); Wait(artwork.DisposeAsync().AsTask()); Wait(database.DisposeAsync().AsTask());
        }
        void Capture(string name)
        {
            Settle(); using var bitmap = window.CaptureRenderedFrame() ?? throw new IOException("No rendered frame.");
            bitmap.Save(Path.Combine(root, name), PngBitmapEncoderOptions.Default);
        }
    }
    private static void Seed(LibraryDatabase db, string root)
    {
        var media = Path.Combine(root, "media"); Directory.CreateDirectory(media);
        var job = db.BeginScanAsync(media, false, CancellationToken.None).GetAwaiter().GetResult();
        for (var offset = 0; offset < 250; offset += 200)
        {
            var rows = Enumerable.Range(offset, Math.Min(200, 250 - offset)).Select(index =>
            {
                var path = Path.Combine(media, $"{index / 100}", $"{index}.flac");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, "fLaC"u8.ToArray());
                return new ScanEntry(new(path, LibraryPath.Key(path), 100, 100), ScanEntryKind.Metadata,
                    new($"{index:D3} · {(index % 3 == 0 ? "Runaway" : index % 3 == 1 ? "The River" : "A Different Kind of Human")}", index < 8 ? "Aurora; Björk" : "Aurora",
                        index < 100 ? "All My Demons Greeting Me as a Friend" : index < 200 ? "A Different Kind of Human" : "What Happened to the Heart?", "Aurora", 180, "FLAC", 96000, 24,
                        index % 100 + 1, 1, index < 100 ? 2018 : 2024));
            }).ToArray();
            db.CommitScanBatchAsync(job, offset / 200 + 1, rows, CancellationToken.None).GetAwaiter().GetResult();
        }
        db.FinishScanAsync(job, ScanStatus.Completed, 250, false, null, CancellationToken.None).GetAwaiter().GetResult();
        var tracks = db.GetTracksAsync(new()).GetAwaiter().GetResult().Items;
        db.SetTrackFavoriteAsync(tracks[0].Id, true, CancellationToken.None).GetAwaiter().GetResult();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (var i = 0; i < 3; i++) db.RecordListeningAsync(new("play" + i, tracks[0].Id, tracks[0].Path, now - 500 + i * 100, now - 410 + i * 100, i == 2 ? 5 : 90, now - 410 + i * 100), CancellationToken.None).GetAwaiter().GetResult();
    }
    private static ArtistInformation Information() => new("ready", DateTimeOffset.UtcNow,
        new("Aurora", "Aurora（欧若拉）是挪威歌手、词曲作者与音乐制作人。她的音乐融合艺术流行、电子乐与民谣，以自然、情感与人与世界的联系为创作主题。\n\n从《Runaway》到《The River》，她在细腻的人声与开阔的编曲之间建立了鲜明的个人表达。\n\n这是一段用于检验原生文字布局、阅读宽度与展开交互的资料夹具。", "zh", "https://zh.wikipedia.org/wiki/Aurora"),
        [new("类型", "音乐人"), new("地区", "挪威"), new("出生日期", "1996-06-15")], [new("Björk", "合作艺人", "https://musicbrainz.org")], [], [new("Wikipedia", "https://zh.wikipedia.org/wiki/Aurora"), new("MusicBrainz", "https://musicbrainz.org")], [], "ready");
    private static void Click(Button button) { button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(); }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> predicate) { var clock = Stopwatch.StartNew(); while (!predicate()) { if (clock.Elapsed.TotalSeconds > 10) throw new TimeoutException("Artist UI check."); Settle(); Thread.Sleep(2); } Settle(); }
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private sealed class LibraryFixture(LibraryDatabase db) : IAlbumLibrary, IArtistLibrary, IAlbumPersonalState, IArtistInformationClient
    {
        public TaskCompletionSource<ArtistInformation> Online { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ArtistInformation> GetArtistInformationAsync(ArtistInformationRequest request, CancellationToken cancellation = default) => Online.Task.WaitAsync(cancellation);
        public Task<ArtistInsights> GetArtistInsightsAsync(string id, ArtistGrouping grouping, CancellationToken cancellation = default) => db.GetArtistInsightsAsync(id, grouping, cancellation);
        public Task<ArtistPage> GetArtistsAsync(ArtistQuery query, CancellationToken cancellation = default) => db.GetArtistsAsync(query, cancellation);
        public Task<TrackPage> GetArtistTracksAsync(ArtistTrackQuery query, CancellationToken cancellation = default) => db.GetArtistTracksAsync(query, cancellation);
        public Task<AlbumPage> GetArtistAlbumsAsync(string id, ArtistGrouping grouping, AlbumQuery query, CancellationToken cancellation = default) => db.GetArtistAlbumsAsync(id, grouping, query, cancellation);
        public Task<AlbumPage> GetAlbumsAsync(AlbumQuery query, CancellationToken cancellation = default) => db.GetAlbumsAsync(query, cancellation);
        public Task<TrackPage> GetAlbumTracksAsync(AlbumTrackQuery query, CancellationToken cancellation = default) => db.GetAlbumTracksAsync(query, cancellation);
        public Task SetAlbumStateAsync(string id, bool favorite, int rating, CancellationToken cancellation) => db.SetAlbumStateAsync(id, favorite, rating, cancellation);
        public Task SetTrackFavoriteAsync(string id, bool favorite, CancellationToken cancellation) => db.SetTrackFavoriteAsync(id, favorite, cancellation);
    }
    internal sealed class AudioFixture : INativeAudio
    {
        private int operation;
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            var value = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openFile" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":600}}",
                _ => "true"
            };
            using var document = JsonDocument.Parse(value); return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
