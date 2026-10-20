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
        var artworkSource = new ArtistArtworkFixture();
        var artwork = new AlbumArtworkCache(artworkSource);
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
            var scroll = detail.FindControl<ScrollViewer>("PageScroll")!;
            Until(() => !model.IsLoading && model.Tracks.Count == 200 && model.Overview.Insights is not null);
            Check(model.CanPlay && model.Overview.IsOnlineLoading, "Local browsing and playback remain available while online metadata waits.");
            var stats = model.Overview.Insights!;
            Check(stats is { FavoriteTracks: 1, Plays: 2, UnplayedAlbums: 2, FirstYear: 2018, LastYear: 2024 } && stats.ListenedSeconds == 180,
                "Statistics use all artist tracks and counted history, not the first page or skipped plays.");
            Check(stats.RelatedArtists.Count == 2 && stats.RelatedArtists[0].Artist.Name == "Björk" && stats.RelatedArtists.Any(relation => relation is { Kind: "sameAlbum", Artist.Name: "Companion" })
                && stats.Collaborations.Count == 8, "Credits distinguish collaborators from artists connected by the same album.");
            Check(stats.Collection is { LosslessCount: 250, CodecTotal: 250, PrimaryAlbums: 2, AppearanceTracks: 50, TopSpec: "FLAC 24bit/96kHz" }
                && stats.Collection.Genres.SequenceEqual(new[] { "Ambient", "Electronic", "Jazz" }), "Collection facts aggregate all tracks beyond the first page, including split genres and quality.");
            Check(stats.CollaborationCredits is { Count: 8 } && stats.CollaborationCredits.All(credit => credit.Artists.SequenceEqual(new[] { "Björk" })), "Core collaboration credits exclude the current artist without reparsing display text in Control.");
            Check(stats.TopTracks.Count == 1 && stats.TopTracks[0].Plays == 2 && stats.DiscoverAlbums.Count == 2, "Top tracks and unplayed albums use exact library relationships.");
            fixture.Online.TrySetResult(Information());
            Until(() => !model.Overview.IsOnlineLoading && model.Overview.HasBiography);
            Check(model.Overview.LibraryFacts.Count == 7 && model.Overview.SourceLinks.Count == 2 && fixture.FactsCalls == 0, "Overview exposes ECHO collection fields and defers the public facts request.");
            Wait(model.Overview.LoadFactsCommand.ExecuteAsync(null));
            Check(model.Overview.PublicFacts is not null && model.Overview.PublicFactRows.Count == 1 && fixture.FactsCalls == 1, "Artist facts use the typed Core client only when requested.");
            Capture("artist-public-facts-light.png");
            Check(model.VisibleTracks.Count == 6, "The overview shows six track previews.");
            Check(detail.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("artist-tab")) == 2, "Artist navigation matches ECHO's overview and albums tabs.");
            model.Overview.Information = Information() with { Links = [new("social network", "https://instagram.com/aurora"), new("social network", "https://instagram.com/aurora-band"), new("biography", "https://en.wikipedia.org/wiki/Aurora"), new("streaming", "https://open.spotify.com/artist/aurora"), new("other databases", "https://discogs.com/artist/aurora")] };
            Check(model.Overview.SourceLinks.Count == 4 && model.Overview.SourceLinks.Count(link => link.Label == "Instagram") == 2
                && model.Overview.SourceLinks.All(link => link.Label != "streaming"), "Source labels follow ECHO's metadata/social filtering and preserve distinct social accounts.");
            Settle();
            Check(model.Overview.SocialAccounts.Select(account => account.Account).SequenceEqual(new[] { "@aurora", "@aurora-band" })
                && model.Overview.MetadataLinks.Count == 2 && detail.FindControl<Grid>("Hero")!.Bounds.Height <= 340,
                "Accounts show platform and handle separately from metadata, and the hero stays compact.");
            Capture("artist-social-profile-light.png");
            model.Overview.Information = Information() with
            {
                Events = [new("Aurora World Tour", "2099-11-03", "Tokyo Garden Theater", "https://www.eventernote.com/events/101", "Eventernote", "19:30", "Tokyo", "Japan", Timezone: "Asia/Tokyo"),
                    new("Aurora Live", "2099-11-04", "Echo Arena", "https://musicbrainz.org/event/00000000-0000-0000-0000-000000000001")],
                EventStatus = "partial", EventFailures = ["eplus"]
            };
            Settle();
            var concerts = detail.FindControl<ArtistEventsView>("ConcertsPanel")!;
            Check(model.Overview.Concerts.Count == 2 && model.Overview.Concerts[0].Time == "19:30 JST"
                && model.Overview.Concerts[1].Time == "时间待定" && model.Overview.ConcertFailures.Contains("eplus"),
                "Concerts retain venue-local time and show pending times and provider failures independently.");
            var content = detail.FindControl<StackPanel>("PageContent")!;
            Check(concerts.TranslatePoint(default, content)!.Value.Y < detail.FindControl<ItemsControl>("PreviewTrackList")!.TranslatePoint(default, content)!.Value.Y,
                "Concerts remain ahead of the expandable song list.");
            scroll.Offset = new(0, concerts.TranslatePoint(default, content)!.Value.Y - 60);
            Capture("artist-concerts-light.png"); shell.IsDark = true; Capture("artist-concerts-dark.png");
            window.Width = 800; Settle(); scroll.Offset = new(0, concerts.TranslatePoint(default, content)!.Value.Y - 60); Capture("artist-concerts-narrow.png");
            window.Width = 1440; shell.IsDark = false; scroll.Offset = default;
            var factsBeforeConcertRefresh = model.Overview.PublicFacts; var insightsBeforeConcertRefresh = model.Overview.Insights;
            Wait(model.Overview.RefreshConcertsCommand.ExecuteAsync(null));
            Check(ReferenceEquals(model.Overview.PublicFacts, factsBeforeConcertRefresh) && ReferenceEquals(model.Overview.Insights, insightsBeforeConcertRefresh)
                && !model.Overview.HasEvents, "Concert refresh updates metadata without resetting public facts or reloading local listening aggregates.");
            model.Overview.Information = Information();
            Wait(model.Tracks[0].ToggleFavoriteCommand.ExecuteAsync(null));
            Check(model.Overview.Insights!.FavoriteTracks == 0, "Successful favorite changes immediately update artist facts.");
            Wait(model.Tracks[0].ToggleFavoriteCommand.ExecuteAsync(null));
            shell.Text.SetLanguage("en-US");
            Check(model.Meta.Contains("tracks") && model.Overview.BiographyAction == "Expand biography" && model.Overview.Facts.Any(fact => fact.Label == "Area"), "Artist copy, formats and profile fields follow the selected language.");
            Capture("artist-english-light.png"); shell.Text.SetLanguage("zh-CN");
            Capture("artist-overview-light.png"); shell.IsDark = true; Capture("artist-overview-dark.png");
            var overview = detail.FindControl<ArtistOverviewView>("OverviewPanel")!;
            var topTracks = overview.FindControl<StackPanel>("TopTracksPanel")!;
            var topTrack = topTracks.GetVisualDescendants().OfType<Button>().Single();
            Check(topTrack.Bounds.Width >= topTracks.Bounds.Width - 2, "Listening tracks fill the column instead of floating in its center.");
            Check(Math.Abs(scroll.TranslatePoint(new Point(scroll.Bounds.Width, 0), window)!.Value.X - window.ClientSize.Width) < 2, "Artist scrolling reaches the window edge.");
            scroll.Offset = new(0, detail.FindControl<ItemsControl>("PreviewTrackList")!.TranslatePoint(default, detail.FindControl<StackPanel>("PageContent")!)!.Value.Y - 100);
            Capture("artist-song-cards-dark.png");
            scroll.Offset = new(0, 540); Capture("artist-information-dark.png");
            shell.IsDark = false; Capture("artist-information-light.png"); shell.IsDark = true;
            window.Width = 800; Capture("artist-narrow-dark.png");
            shell.IsDark = false; Capture("artist-narrow-light.png");
            Check(detail.FindControl<ArtistOverviewView>("OverviewPanel")!.FindControl<Avalonia.Controls.Primitives.UniformGrid>("ListeningFacts")!.Columns == 2, "Narrow statistics wrap without losing facts.");
            window.Width = 1440; scroll.Offset = default;
            Click(detail.FindControl<Button>("TracksToggleButton")!); Check(!model.ShowTracks, "Songs collapse within the overview.");
            Click(detail.FindControl<Button>("TracksToggleButton")!);
            Click(detail.FindControl<Button>("ExpandTracksButton")!);
            Check(model.IsTracks && model.VisibleTracks.Count == 200 && detail.GetVisualDescendants().OfType<ListBoxItem>().Count() < 30, "Full track pages remain virtualized.");
            Wait(model.NextCommand.ExecuteAsync(null)); Check(model.Tracks.Count == 50, "Artist paging reaches tracks outside the first page.");
            Wait(model.PreviousCommand.ExecuteAsync(null)); Wait(model.ShuffleCommand.ExecuteAsync(null));
            Check(session.Queue.Count == 250, $"Artist shuffle uses the complete bounded collection: queue={session.Queue.Count}, error={model.Error}.");
            scroll.Offset = new(0, detail.FindControl<ListBox>("TrackList")!.TranslatePoint(default, detail.FindControl<StackPanel>("PageContent")!)!.Value.Y - 100);
            Wait(model.PlayTrackAsync(model.Tracks[0].Row));
            Until(() => detail.GetVisualDescendants().OfType<ArtistTrackCard>().Any(card => card.Track?.Id == model.Tracks[0].Row.Id && card.IsPlaying));
            Capture("artist-tracks-light.png");
            scroll.Offset = default;
            Click(detail.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("artist-tab") && button.Tag?.ToString() == "1"));
            Until(() => detail.GetVisualDescendants().OfType<AlbumWallViewport>().Any(wall => wall.RealizedCards > 0));
            Check(detail.GetVisualDescendants().OfType<AlbumWallViewport>().Single().RealizedCards <= 48, "Album realization remains bounded.");
            Capture("artist-albums-light.png");
            var copiedArtist = stats.RelatedArtists[0].Artist;
            var opened = false; detail.OpenArtist += target => opened = target.Id == copiedArtist.Id;
            Click(detail.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("artist-tab") && button.Tag?.ToString() == "0"));
            model.Overview.Information = Information() with { Biography = null, Facts = [new("成立日期", "2018-08-13")], Relations = [] };
            model.Overview.Insights = stats with { RelatedArtists = [], Collaborations = [] };
            scroll.Offset = new(0, 440); Capture("artist-sparse-light.png");
            model.Overview.Insights = stats with { Plays = 0, TopTracks = [] };
            Settle(); Check(!overview.FindControl<Grid>("ListeningBody")!.IsVisible, "Missing history uses the same empty state instead of invented statistics.");
            Capture("artist-no-history-light.png");
            model.Overview.Information = Information(); model.Overview.Insights = stats;
            Settle();
            model.Overview.Information = Information() with { Biography = Information().Biography! with { ArtworkReference = "fixture" } };
            Until(() => artworkSource.OnlinePortraits > 0);
            Until(() => detail.FindControl<Image>("Portrait")!.Source is not null);
            Check(artworkSource.LocalPortraits > 0 && artworkSource.OnlinePortraits == 1,
                "Missing local covers and portraits fall back to the matched online image through the shared artwork worker.");
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
            Settle();
            Check(!window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsVisible && block.Text?.Contains("artist.content.", StringComparison.Ordinal) == true), "Artist content labels resolve in the selected language.");
            using var bitmap = window.CaptureRenderedFrame() ?? throw new IOException("No rendered frame.");
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
                        index < 100 ? "All My Demons Greeting Me as a Friend" : index < 200 ? "A Different Kind of Human" : "What Happened to the Heart?", index < 200 ? "Aurora" : "Companion", 180, "FLAC", 96000, 24,
                        index % 100 + 1, 1, index < 100 ? 2018 : 2024, Genre: index < 225 ? "Ambient / Electronic" : "Jazz"));
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
    private static void Click(Button button)
    {
        if (button.Command is { } command) command.Execute(button.CommandParameter);
        else button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle();
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> predicate) { var clock = Stopwatch.StartNew(); while (!predicate()) { if (clock.Elapsed.TotalSeconds > 10) throw new TimeoutException("Artist UI check."); Settle(); Thread.Sleep(2); } Settle(); }
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private sealed class LibraryFixture(LibraryDatabase db) : IAlbumLibrary, IArtistLibrary, IAlbumPersonalState, IArtistInformationClient
    {
        public TaskCompletionSource<ArtistInformation> Online { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FactsCalls;
        public async Task<ArtistInformation> GetArtistInformationAsync(ArtistInformationRequest request, CancellationToken cancellation = default)
        {
            var information = await Online.Task.WaitAsync(cancellation);
            if (!request.IncludeFacts) return information;
            FactsCalls++;
            return information with { PublicFacts = new("Aurora", "https://www.wikidata.org/wiki/Q42", "wikipedia", ["AURORA"], [new("国籍", "挪威")], []) };
        }
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
