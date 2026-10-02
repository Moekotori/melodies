using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Application.Playback;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Home;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playback;
using SkiaSharp;

internal static class HomeUiSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output); var clock = Stopwatch.StartNew();
        var opening = LibraryDatabase.OpenAsync(Path.Combine(output, $"home-{Guid.NewGuid():N}.sqlite")); Wait(opening);
        var database = opening.GetAwaiter().GetResult();
        var native = new HomeNative(); var runtime = new PlaybackRuntime(native);
        MainWindow? window = null;
        try
        {
            Seed();
            var get = database.GetHomeAsync(DateOnly.FromDateTime(DateTime.Now)); Wait(get);
            Check(get.Result.Library is { Tracks: 36, Albums: 12, Artists: 4, Folders: 12 }, "real library summary");
            Check(get.Result.RecentAdded.Count == 8 && get.Result.Recommended.Count == 7 && get.Result.Listening.Tracks.Count == 6, "bounded dashboard");
            var eventTrack = get.Result.LastTrack!; var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            var entry = new ListeningEntry("idempotent", eventTrack.Id, eventTrack.Path, now, now, 24);
            Wait(database.RecordListeningAsync(entry)); Wait(database.RecordListeningAsync(entry with { PlayedSeconds = 12 }));
            var before = database.GetHomeAsync(DateOnly.FromDateTime(DateTime.Now)); Wait(before);
            Check(before.Result.Listening.WeekPlays == get.Result.Listening.WeekPlays + 1 && Math.Abs(before.Result.Listening.WeekSeconds - get.Result.Listening.WeekSeconds - 24) < .01, "idempotent monotonic listening write");
            var canceled = new CancellationToken(true);
            try { Wait(database.GetHomeAsync(DateOnly.FromDateTime(DateTime.Now), canceled)); throw new InvalidOperationException("cancellation ignored"); } catch (OperationCanceledException) { }
            window = DesktopComposition.CreateWindow(false, false, database, new LocalAlbumArtwork(), runtime);
            window.Width = 1440; window.Height = 1050;
            var shell = (ShellViewModel)window.DataContext!;
            shell.Motion.SelectedChoice = shell.Motion.Choices.First(item => item.Mode == MotionMode.Off);
            window.Show();
            var home = window.GetVisualDescendants().OfType<HomeView>().Single(); var model = home.Model!;
            Until(() => !model.IsLoading && model.HasRecommendations);
            Check(model.Error.Length == 0 && model.Recent.Count == 4, "loaded home");
            var first = model.Recent[0].Id;
            Click(home.FindControl<Button>("NextRecentButton")!); Check(model.RecentPage == 1 && model.Recent[0].Id != first, "real recent next-page input");
            Click(home.FindControl<Button>("PreviousRecentButton")!); Check(model.RecentPage == 0, "previous recent page");
            Click(home.FindControl<Button>("ContinueButton")!); Until(() => runtime.LocalZone.Session.Snapshot.OperationId > 0 && !model.IsBusy);
            var current = runtime.LocalZone.Session.Snapshot.Current!.ItemId;
            native.Emit("audio.started", runtime.LocalZone.Session.Snapshot.OperationId);
            Until(() => runtime.LocalZone.Session.Snapshot.Status == PlaybackStatus.Playing);
            Thread.Sleep(120); native.Position(runtime.LocalZone.Session.Snapshot.OperationId, 5760);
            Until(() => runtime.LocalZone.Session.Snapshot.PositionSeconds > 0);
            Until(() => home.GetVisualDescendants().OfType<HomeNowLyrics>().Any(view => view.FindControl<Button>("OpenLyrics")!.IsVisible));
            Wait(runtime.LocalZone.Session.SeekAsync(100));
            Thread.Sleep(120); native.Position(runtime.LocalZone.Session.Snapshot.OperationId, 5760);
            Wait(runtime.LocalZone.Session.PauseAsync());
            Wait(model.ContinueCommand.ExecuteAsync(null));
            Check(runtime.LocalZone.Session.Snapshot.Current!.ItemId == current && runtime.LocalZone.Session.Queue.Count == 1, "continue keeps queue occurrence");
            native.Emit("audio.started", runtime.LocalZone.Session.Snapshot.OperationId);
            Wait(runtime.LocalZone.Session.PauseAsync());
            Until(() => !model.IsLoading);
            var recorded = database.GetHomeAsync(DateOnly.FromDateTime(DateTime.Now)); Wait(recorded);
            Check(recorded.Result.Listening.WeekPlays == before.Result.Listening.WeekPlays + 1 && recorded.Result.Listening.WeekSeconds - before.Result.Listening.WeekSeconds < 3, "recorder excludes seek and counts resume once");
            shell.SetRecentTabCommand.Execute("played"); Check(model.HasRecent, "persistent recent played");
            shell.SetRecentTabCommand.Execute("added");
            Until(() => home.GetVisualDescendants().OfType<HomeAlbumCard>().Any(card => card.FindControl<Image>("Cover")!.Source is not null));
            Capture("home-light.png"); shell.IsDark = true; Capture("home-dark.png");
            var scroll = home.FindAncestorOfType<ScrollViewer>()!; scroll.Offset = new(0, 850); Capture("home-insights-dark.png");
            scroll.Offset = new(0, 1400); Capture("home-profile-dark.png"); scroll.Offset = new(0, 0);
            model.OpenAlbumCommand.Execute(model.Recent[0]); Until(() => home.GetVisualDescendants().OfType<DetailNavigator>().Any());
            var detail = home.GetVisualDescendants().OfType<DetailNavigator>().Single(); Check(detail.Content is Echo.Desktop.Features.Albums.AlbumDetailView, "album opens real detail"); detail.GoBack();
            model.OpenArtistCommand.Execute("林间"); Until(() => home.GetVisualDescendants().OfType<DetailNavigator>().Any());
            detail = home.GetVisualDescendants().OfType<DetailNavigator>().Single(); Check(detail.Content is Echo.Desktop.Features.Artists.ArtistDetailView, "artist opens real detail"); detail.GoBack();
            Click(home.FindControl<Button>("RandomButton")!); Until(() => !model.IsBusy && runtime.LocalZone.Session.Queue.Count == 36);
            Check(runtime.LocalZone.Session.Queue.Select(item => item.TrackId).Distinct().Count() == 36, "bounded random queue");
            window.Width = 1000; shell.IsSidebarExpanded = false; shell.IsDark = false; Capture("home-narrow-light.png");
            shell.Text.SetLanguage("en-US"); Capture("home-english.png"); Check(model.HeroLabel == "Now playing", "language switch");
            shell.NavigateCommand.Execute("albums"); Settle(); Check(model.Recent.Count == 0, "home snapshot released on navigation");
            shell.NavigateCommand.Execute("home"); Until(() => window.GetVisualDescendants().OfType<HomeView>().Any(page => page.Model?.HasRecommendations == true));
            Console.WriteLine($"Home passed in {clock.Elapsed.TotalSeconds:F2}s: SQLite counts/bounds/idempotency/cancellation, continue/seek/resume, native pointer paging/random, detail navigation, themes/narrow/language/release. Audio is a protocol fixture.");
        }
        finally
        {
            if (window is not null) { window.Close(); Until(() => !window.IsVisible); }
            Wait(runtime.DisposeAsync().AsTask()); Wait(database.DisposeAsync().AsTask());
        }

        void Seed()
        {
            string[] titles = ["夜航", "山海之间", "晴日", "月光海岸", "Slow Morning", "Echoes", "四季", "星际旅程", "远方的风", "城市漫游", "归途", "Before Dawn"];
            string[] artists = ["林间", "陈宁", "南风", "Mira"];
            string[] colors = ["#23334A", "#466358", "#BB783F", "#514B67", "#B69E72", "#467286"];
            var root = Path.Combine(output, "media"); Directory.CreateDirectory(root);
            var begin = database.BeginScanAsync(root, false, CancellationToken.None); Wait(begin);
            var entries = new List<ScanEntry>(36);
            for (var album = 0; album < titles.Length; album++)
            {
                var folder = Path.Combine(root, $"{album:D2}"); Directory.CreateDirectory(folder);
                using (var bitmap = new SKBitmap(320, 320))
                {
                    using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColor.Parse(colors[album % colors.Length]));
                    using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(230, 219, 194, 160) };
                    canvas.DrawCircle(160 + album * 3, 140, 75, paint); paint.Color = new SKColor(10, 24, 36, 110); canvas.DrawRect(0, 200, 320, 120, paint);
                    using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(Path.Combine(folder, "cover.png")); data.SaveTo(file);
                }
                for (var track = 0; track < 3; track++)
                {
                    var path = Path.Combine(folder, $"{track + 1}.flac"); File.WriteAllBytes(path, "fLaC"u8.ToArray());
                    File.WriteAllText(Path.ChangeExtension(path, "lrc"), "[00:00.00]风从山海之间吹来\n[00:30.00]沿着月光继续远行\n[01:20.00]听见这城市的回声\n[02:00.00]星光落在归途");
                    entries.Add(new(new(path, LibraryPath.Key(path), 4, 123), ScanEntryKind.Metadata,
                        new(track == 0 ? titles[album] : titles[album] + " · " + (track + 1), artists[album % 4], titles[album], artists[album % 4], 240, "flac", 96000, 24, track + 1, 1, 2026)));
                }
            }
            Wait(database.CommitScanBatchAsync(begin.Result, 1, entries, CancellationToken.None)); Wait(database.FinishScanAsync(begin.Result, ScanStatus.Completed, 36, false, null, CancellationToken.None));
            var tracks = database.GetTracksAsync(new(Limit: 36)); Wait(tracks);
            for (var index = 0; index < 18; index++)
            {
                var track = tracks.Result.Items[index % 12]; var started = DateTimeOffset.Now.AddDays(-index * 2).ToUnixTimeSeconds();
                Wait(database.RecordListeningAsync(new("seed-" + index, track.Id, track.Path, started, started + 180, 180 + index * 8)));
            }
        }
        void Capture(string name) { Settle(); using var image = window!.CaptureRenderedFrame() ?? throw new IOException("No rendered frame."); image.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); }
        void Click(Button button)
        { var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window!)!.Value; window!.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle(); }
        void Until(Func<bool> ready) { while (!ready()) { if (clock.Elapsed.TotalSeconds > 25) throw new TimeoutException("home smoke"); Settle(); Thread.Sleep(2); } Settle(); }
        void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private sealed class HomeNative : INativeAudio
    {
        private int operation;
        public event Action<string, JsonElement>? Notification;
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Emit(string method, long op) { using var doc = JsonDocument.Parse($"{{\"operationId\":{op}}}"); Notification?.Invoke(method, doc.RootElement.Clone()); }
        public void Position(long op, long frames) { using var doc = JsonDocument.Parse($"{{\"operationId\":{op},\"framesPlayed\":{frames}}}"); Notification?.Invoke("audio.position", doc.RootElement.Clone()); }
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            var value = method switch { "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}", "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}", "audio.openFile" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":240}}", _ => "true" };
            using var document = JsonDocument.Parse(value); return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
