using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Artists;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playback;
using SkiaSharp;

internal static class DetailUiSmoke
{
    public static void Run(string output, bool layoutOnly = false)
    {
        Directory.CreateDirectory(output);
        var clock = Stopwatch.StartNew();
        var opening = LibraryDatabase.OpenAsync(Path.Combine(output, "details.sqlite")); Wait(opening);
        var database = opening.GetAwaiter().GetResult(); Seed(database, output);
        var native = new DetailNative(); var session = new PlaybackSessionCoordinator(native);
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session));
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork());
        var onlineFixture = new AlbumOnlineFixture();
        var shell = new ShellViewModel { AlbumInformation = onlineFixture }; shell.AttachPlayback(player); player.Queue.Artwork = artwork;
        shell.AttachLibrary(database, artwork, details: new LocalDetailResources());
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 1000 };
        try
        {
            window.Show(); shell.NavigateCommand.Execute("albums");
            Until(() => window.GetVisualDescendants().OfType<AlbumCard>().Any(card => card.FindControl<Button>("OpenButton")!.IsEnabled));
            var wall = window.GetVisualDescendants().OfType<AlbumsView>().Single();
            var card = wall.GetVisualDescendants().OfType<AlbumCard>().First(card => card.FindControl<TextBlock>("Artist")!.Text == "周杰伦" && card.FindControl<TextBlock>("AlbumTitle")!.Text == "A 星际旅程"); Click(card.FindControl<Button>("OpenButton")!);
            Until(() => wall.GetVisualDescendants().OfType<AlbumDetailView>().Any());
            var detail = wall.GetVisualDescendants().OfType<AlbumDetailView>().Single();
            var model = (AlbumDetailViewModel)detail.DataContext!;
            Until(() => model.Tracks.Count > 0 && !model.IsLoading);
            if (layoutOnly) { AlbumDetailLayoutSmoke.Run(window, shell, detail, output); return; }
            Check(model.Tracks.Count == 200 && wall.Viewport.RealizedCards == 0, $"bounded album page: {model.Tracks.Count} tracks, {wall.Viewport.RealizedCards} wall cards, {model.Album.Artist}");
            Check(detail.GetVisualDescendants().OfType<AlbumTrackRow>().Count() < 40, "album tracks remain virtualized under page scrolling");
            var pageScroll = detail.FindControl<ScrollViewer>("PageScroll")!;
            var scrollRight = pageScroll.TranslatePoint(new Point(pageScroll.Bounds.Width, 0), window)!.Value.X;
            Check(Math.Abs(scrollRight - window.ClientSize.Width) < 2, "album scrollbar reaches the window edge");
            Check(wall.Margin == default, "album detail owns its content inset");
            var title = detail.FindControl<TextBlock>("AlbumTitle")!;
            Check(title.LineHeight >= title.FontSize * 1.2, "album title reserves full glyph height");
            pageScroll.Offset = new(0, 6000); Settle();
            Check(pageScroll.Offset.Y > 5000 && detail.GetVisualDescendants().OfType<AlbumTrackRow>().Count() < 40, "whole-page scrolling keeps album rows bounded");
            pageScroll.Offset = default; Settle();
            var rows = model.Tracks;
            model.Descending = true; model.SortIndex = 2;
            Until(() => !model.IsLoading && !ReferenceEquals(rows, model.Tracks));
            Check(model.Tracks.Zip(model.Tracks.Skip(1)).All(pair => pair.First.Row.DurationSeconds >= pair.Second.Row.DurationSeconds), "database sorting");
            Wait(model.NextCommand.ExecuteAsync(null)); Check(model.Tracks.Count == 50, "second album page");
            Wait(model.PreviousCommand.ExecuteAsync(null)); Check(model.Tracks.Count == 200, "previous album page");
            Click(detail.FindControl<Button>("Rate3")!); Until(() => model.Rating == 3 && !model.IsBusy);
            Wait(model.ToggleFavoriteCommand.ExecuteAsync(null));
            var stored = database.GetAlbumsAsync(new()); Wait(stored);
            Check(stored.Result.Items.First(album => album.Id == model.Album.Id) is { Rating: 3, Favorite: true }, "personal state persistence");
            Wait(model.PlayTrackAsync(model.Tracks[0].Row)); Check(session.Queue.Count == 250, "album play queue");
            Until(() => model.Tracks[0].IsPlaying);
            var playingRow = detail.GetVisualDescendants().OfType<AlbumTrackRow>().First(row => ReferenceEquals(row.DataContext, model.Tracks[0]));
            var favoriteButton = playingRow.FindControl<Button>("FavoriteButton")!;
            var favoritePoint = favoriteButton.TranslatePoint(new Point(15, 15), window)!.Value;
            var operation = session.Snapshot.OperationId;
            window.MouseMove(favoritePoint); window.MouseDown(favoritePoint, MouseButton.Left); window.MouseUp(favoritePoint, MouseButton.Left);
            Until(() => model.Tracks[0].Favorite);
            Check(session.Snapshot.OperationId == operation, "row favorite must not also play the parent row");
            Wait(model.AddTrackAsync(model.Tracks[1].Row, true)); Check(session.Queue.Any(track => track.QueuedNext), "next-up action");
            var outsideRead = database.GetAlbumTracksAsync(new(model.Album.Id, 200, Sort: DetailTrackSort.Duration, Descending: true)); Wait(outsideRead);
            var outsideTrack = outsideRead.Result.Items[0];
            Wait(database.SetTrackFavoriteAsync(outsideTrack.Id, true, CancellationToken.None));
            var likedId = model.Tracks[0].Row.Id;
            Tab(detail, 5); Until(() => !model.IsLoading && model.Tracks.Count == 2);
            Check(model.Tracks.Any(track => track.Row.Id == likedId) && model.Tracks.Any(track => track.Row.Id == outsideTrack.Id) && model.Tracks.All(track => track.Favorite), "liked tab includes favorites beyond the loaded page");
            Check(model.TrackPanelCountText.StartsWith("2") && model.TrackPanelDuration != model.DurationLabel, "liked count and duration use the filtered album total");
            Check(!detail.FindControl<StackPanel>("OnlinePanels")!.IsVisible, "online panels stay hidden in the liked tab");
            Capture("album-liked-light.png");
            while (model.Tracks.Count > 0)
            {
                var removed = model.Tracks[0].Row.Id;
                Wait(model.Tracks[0].ToggleFavoriteCommand.ExecuteAsync(null));
                Until(() => !model.IsLoading && model.Tracks.All(track => track.Row.Id != removed));
            }
            Check(model.IsEmpty, "unliking the last track updates the empty state");
            Tab(detail, 0); Until(() => !model.IsLoading && model.Tracks.Count == 200);
            Tab(detail, 1); Until(() => detail.FindControl<WrapPanel>("Attachments")!.Children.Count >= 3);
            Check(detail.FindControl<WrapPanel>("Attachments")!.Children.Count >= 4, "gallery discovers bounded Scans subfolders");
            var attachment = detail.FindControl<WrapPanel>("Attachments")!.Children.OfType<Button>().First();
            Click(attachment); Until(() => detail.FindControl<Grid>("CoverPreview")!.IsVisible);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle();
            Check(!detail.FindControl<Grid>("CoverPreview")!.IsVisible, "attachment preview closes without navigating away");
            Check(!detail.FindControl<StackPanel>("OnlinePanels")!.IsVisible, "online panels stay hidden in the gallery tab");
            Capture("album-attachments-light.png");
            Check(onlineFixture.Calls == 0, "online providers are deferred until their tab opens");
            Tab(detail, 2);
            var online = (AlbumOnlineViewModel)detail.FindControl<AlbumOnlineView>("OnlinePanel")!.DataContext!;
            Until(() => !online.IsLoading && online.HasRelease);
            Check(online.HasCredits && online.HasLinks, "sources show release facts, credits and links");
            pageScroll.Offset = new(0, detail.FindControl<StackPanel>("OnlinePanels")!.TranslatePoint(default, detail.FindControl<Grid>("PageContent")!)!.Value.Y); Settle(); Capture("album-sources-light.png"); pageScroll.Offset = default; Settle();
            Tab(detail, 3); Until(() => online.HasVersions && !online.IsLoading && !detail.FindControl<TextBlock>("VersionsStatus")!.Text!.StartsWith("正在"));
            pageScroll.Offset = new(0, detail.FindControl<StackPanel>("OnlinePanels")!.TranslatePoint(default, detail.FindControl<Grid>("PageContent")!)!.Value.Y); Settle(); Capture("album-versions-light.png"); pageScroll.Offset = default; Settle();
            Tab(detail, 4); Until(() => online.HasArticles && !online.IsLoading && detail.FindControl<SelectableTextBlock>("Description")!.Text == "星际旅程的本地专辑介绍。");
            pageScroll.Offset = new(0, detail.FindControl<StackPanel>("OnlinePanels")!.TranslatePoint(default, detail.FindControl<Grid>("PageContent")!)!.Value.Y); Settle(); Capture("album-information-light.png"); pageScroll.Offset = default; Settle();
            Check(onlineFixture.Calls == 1, "online tabs share one bounded result");
            Until(() => detail.FindControl<SelectableTextBlock>("Description")!.Text == "星际旅程的本地专辑介绍。");
            Tab(detail, 3); Until(() => detail.FindControl<StackPanel>("Versions")!.Children.Count == 0);
            Tab(detail, 0); Until(() => !model.IsLoading && model.Tracks.Count == 200); Capture("album-detail-light.png"); shell.IsDark = true; Capture("album-detail-dark.png");
            shell.IsDark = false; Settle();
            var moreButton = detail.FindControl<Button>("MoreButton")!;
            moreButton.Flyout!.ShowAt(moreButton); Settle();
            CaptureControl((Control)detail.FindControl<Button>("AlbumFavoriteButton")!.Parent!, "album-actions-light.png");
            CaptureControl(detail.FindControl<DetailSortButton>("TrackSort")!, "album-sort-control-light.png");
            shell.IsDark = true; Settle();
            var sortControl = detail.FindControl<DetailSortButton>("TrackSort")!;
            Check(sortControl.IsInline, "album more menu exposes sorting directly");
            var sortMenu = sortControl.FindControl<StackPanel>("SortOptions")!;
            Click(sortMenu.GetVisualDescendants().OfType<Button>().First(button => button.Tag?.ToString() == "1"));
            Until(() => model.SortIndex == 1 && !model.IsLoading);
            Click(sortMenu.GetVisualDescendants().OfType<Button>().First(button => button.Tag?.ToString() == "False"));
            Until(() => !model.Descending && !model.IsLoading);
            Check(sortControl.SelectedIndex == 1 && !sortControl.Descending, "inline sort binds field and direction");
            moreButton.Flyout.Hide();
            Capture("album-detail-sort-dark.png");
            Until(() => detail.FindControl<Button>("CoverButton")!.IsEnabled);
            Click(detail.FindControl<Button>("CoverButton")!); Capture("album-cover-preview.png");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle();
            Check(!detail.FindControl<Grid>("CoverPreview")!.IsVisible && wall.GetVisualDescendants().OfType<AlbumDetailView>().Any(), "Escape closes cover preview before navigating back");
            window.Width = 800; Settle();
            Check(detail.FindControl<Border>("Facts")!.IsVisible && Grid.GetRow(detail.FindControl<Border>("Facts")!) == 2, "narrow layout preserves rating and facts");
            Capture("album-detail-narrow-dark.png");
            shell.IsDark = false; Capture("album-detail-narrow-light.png");
            // Inspect the online reading layouts in both themes at the narrow viewport.
            foreach (var (tab, name) in new[] { (2, "sources"), (3, "versions"), (4, "information") })
            {
                Tab(detail, tab); Settle();
                pageScroll.Offset = new(0, detail.FindControl<StackPanel>("OnlinePanels")!.TranslatePoint(default, detail.FindControl<Grid>("PageContent")!)!.Value.Y);
                Settle(); Capture($"album-{name}-narrow-light.png");
                shell.IsDark = true; Capture($"album-{name}-narrow-dark.png"); shell.IsDark = false;
            }
            pageScroll.Offset = default; Settle();
            window.Width = 1440; Settle();
            Click(detail.FindControl<Button>("ArtistLink")!);
            Until(() => wall.GetVisualDescendants().OfType<ArtistDetailView>().Any());
            Check(wall.Margin == default, "artist detail keeps its scroll viewport at the window edge");
            var artist = wall.GetVisualDescendants().OfType<ArtistDetailView>().Single();
            var artistModel = (ArtistDetailViewModel)artist.DataContext!;
            Until(() => artistModel.Tracks.Count == 200 && !artistModel.IsLoading);
            Check(artistModel.Artist.TrackCount == 450, "album artist resolution");
            Capture("artist-detail-dark.png"); shell.IsDark = false; Capture("artist-detail-light.png");
            artist.FindControl<ScrollViewer>("PageScroll")!.Offset = new(0, 620); Settle(); Capture("artist-overview-light.png");
            Check(artist.GetVisualDescendants().OfType<ListBoxItem>().Count() < 40, "realized artist tracks");
            Wait(artistModel.AddToQueueCommand.ExecuteAsync(null)); Check(session.Queue.Count == 701, "artist queue add");
            Wait(artistModel.ShuffleCommand.ExecuteAsync(null)); Check(session.Queue.Count == 450, "artist shuffle queue");
            Tab(artist, 1); Until(() => artist.GetVisualDescendants().OfType<AlbumWallViewport>().Any(view => view.RealizedCards > 0));
            var artistWall = artist.GetVisualDescendants().OfType<AlbumWallViewport>().Single(); Check(artistWall.RealizedCards <= 48, "artist albums bounded");
            Capture("artist-albums-light.png");
            var navigator = wall.GetVisualDescendants().OfType<DetailNavigator>().Single(); navigator.GoBack();
            Check(navigator.Content is AlbumDetailView && artistModel.Tracks.Count == 0, "back releases old detail");
            navigator.GoBack(); Until(() => wall.Viewport.RealizedCards > 0);
            shell.NavigateCommand.Execute("artists");
            Until(() => window.GetVisualDescendants().OfType<ArtistsView>().Any());
            var artists = window.GetVisualDescendants().OfType<ArtistsView>().Single(); Until(() => artists.Model.Total > 0 && artists.Viewport.RealizedCards > 0);
            Check(artists.Model.ResidentArtists <= 600, "artist wall data bound");
            shell.NavigateCommand.Execute("home"); Settle(); Check(artists.Viewport.RealizedCards == 0, "route release");
            Check(artwork.Bytes <= 32 * 1024 * 1024, "shared artwork budget");
            Console.WriteLine($"Detail UI passed in {clock.Elapsed.TotalMilliseconds:F0}ms: real SQLite sorting/pages, rating/favorite, queue/next/shuffle, attachments/description/versions, artist navigation, bounded realized controls, themes and release. Audio host is a protocol fixture; no device playback acceptance.");
        }
        finally
        {
            window.ReleaseLibraryPage(); window.Close(); player.Detach();
            foreach (var task in new[] { session.DisposeAsync().AsTask(), artwork.DisposeAsync().AsTask(), database.DisposeAsync().AsTask() })
            {
                var release = Stopwatch.StartNew();
                while (!task.IsCompleted && release.Elapsed.TotalSeconds < 3) { Settle(); Thread.Sleep(2); }
                if (task.IsCompleted) task.GetAwaiter().GetResult();
            }
        }

        void Capture(string name)
        {
            window.MouseMove(new Point(300, 20));
            Settle(); using var bitmap = window.CaptureRenderedFrame() ?? throw new IOException("Render unavailable.");
            bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); Console.WriteLine(Path.Combine(output, name));
        }
        void CaptureControl(Control control, string name)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(control.Bounds.Width), (int)Math.Ceiling(control.Bounds.Height)), new Vector(96, 96));
            // A standalone control render has no parent backdrop. Use the real page brush
            // for the capture, then restore the transparent control immediately.
            var page = (TopLevel.GetTopLevel(control) as Window)?.Background ?? Avalonia.Media.Brushes.White;
            if (control is Panel panel)
            { var background = panel.Background; panel.Background = page; Settle(); bitmap.Render(control); panel.Background = background; }
            else if (control is UserControl view)
            { var background = view.Background; view.Background = page; Settle(); bitmap.Render(control); view.Background = background; }
            else bitmap.Render(control);
            bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
        void Until(Func<bool> ready) { var waiting = Stopwatch.StartNew(); while (!ready()) { if (waiting.Elapsed.TotalSeconds > 10) throw new TimeoutException("detail smoke"); Settle(); Thread.Sleep(2); } Settle(); }
        void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
        void Seed(LibraryDatabase db, string root)
        {
            var media = Path.Combine(root, "media"); Directory.CreateDirectory(media);
            using (var bitmap = new SKBitmap(320, 320))
            {
                using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColor.Parse("#253f64"));
                using var paint = new SKPaint { Color = SKColor.Parse("#dac8a8"), IsAntialias = true }; canvas.DrawCircle(220, 110, 70, paint);
                using var picture = SKImage.FromBitmap(bitmap); using var data = picture.Encode(SKEncodedImageFormat.Png, 100);
                using var file = File.Create(Path.Combine(media, "cover.png")); data.SaveTo(file);
            }
            File.WriteAllText(Path.Combine(media, "album.txt"), "星际旅程的本地专辑介绍。");
            File.WriteAllText(Path.Combine(media, "booklet.txt"), "专辑内页");
            Directory.CreateDirectory(Path.Combine(media, "Scans"));
            File.Copy(Path.Combine(media, "cover.png"), Path.Combine(media, "Scans", "booklet-01.png"), true);
            var begin = db.BeginScanAsync(media, false, CancellationToken.None); Wait(begin); var job = begin.Result;
            for (var offset = 0; offset < 451; offset += 200)
            {
                var entries = Enumerable.Range(offset, Math.Min(200, 451 - offset)).Select(index =>
                {
                    var path = Path.Combine(media, $"{index:D4}.flac");
                    File.WriteAllBytes(path, "fLaC"u8.ToArray());
                    var artist = index == 450 ? "其他艺术家" : "周杰伦";
                    return new ScanEntry(new(path, LibraryPath.Key(path), 100, 123), ScanEntryKind.Metadata,
                        new($"曲目 {index:D3}", artist, index < 250 || index == 450 ? "A 星际旅程" : "B 夜航", artist,
                            180 + index, "FLAC", 96000, 24, index + 1, 1, 2026));
                }).ToArray();
                Wait(db.CommitScanBatchAsync(job, offset / 200 + 1, entries, CancellationToken.None));
            }
            Wait(db.FinishScanAsync(job, ScanStatus.Completed, 451, false, null, CancellationToken.None));
        }
    }
    private static void Tab(Control view, int tab) => Click(view.GetVisualDescendants().OfType<Button>().First(button =>
        (button.Classes.Contains("detail-tab") || button.Classes.Contains("artist-tab")) && button.Tag?.ToString() == tab.ToString()));
    private static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class DetailNative : INativeAudio
    {
        private int _operation;
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            var value = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openFile" or "audio.seek" => $"{{\"operationId\":{++_operation},\"sampleRate\":48000,\"durationSeconds\":600}}",
                _ => "true"
            };
            using var document = JsonDocument.Parse(value); return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
