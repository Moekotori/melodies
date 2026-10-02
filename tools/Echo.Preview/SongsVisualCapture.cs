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
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Songs;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class SongsVisualCapture
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var opening = LibraryDatabase.OpenAsync(Path.Combine(output, "demo.sqlite")); Wait(() => opening.IsCompleted);
        var db = opening.GetAwaiter().GetResult(); Seed(db, output);
        var shell = new ShellViewModel(); shell.Text.SetLanguage("zh-CN");
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork()); shell.AttachLibrary(db, artwork);
        var session = new PlaybackSessionCoordinator(new CaptureNative());
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session), shell.Text); shell.AttachPlayback(player);
        player.Queue.Artwork = artwork;
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        try
        {
            window.Show(); shell.NavigateCommand.Execute("songs"); Pump();
            var view = window.GetVisualDescendants().OfType<SongsView>().Single(); var model = view.Model!;
            Wait(() => !model.IsLoading && model.Items.Count > 0 && artwork.Count > 0); Pump();
            var row = view.GetVisualDescendants().OfType<SongTrackRow>().First();
            Check(view.GetVisualDescendants().OfType<SongTrackRow>().Count() < 40, "virtualized rows");
            Click(row.FindControl<Button>("CoverPlay")!); Wait(() => session.Snapshot.Current is not null); Pump();
            Check(session.Queue.Count == model.Items.Count, "cover play action");
            Wait(() => artwork.Count >= view.GetVisualDescendants().OfType<SongTrackRow>().Count());
            window.MouseMove(new Point(260,100)); Pump(); Capture("songs-light.png");
            var filter = view.FindControl<Button>("FilterButton")!; Click(filter); Pump();
            Check(filter.Flyout!.IsOpen, "filter opens");
            var panel = window.GetVisualDescendants().OfType<LibraryFilterView>().Single();
            var rate = panel.FindControl<ComboBox>("RateChoice")!;
            Click(rate); Pump(); Check(rate.IsDropDownOpen, "nested choices open");
            rate.SelectedIndex = 4; rate.IsDropDownOpen = false; Wait(() => !model.IsLoading);
            Check(filter.Flyout.IsOpen && model.Items.All(track => track.SampleRate == 96000), "nested filter stays open and applies");
            var genreChoice = panel.FindControl<ComboBox>("GenreChoice")!;
            Click(genreChoice); Wait(() => genreChoice.ItemsSource!.Cast<FilterChoice>().Any(choice => choice.Value == "Pop"));
            Check(genreChoice.IsDropDownOpen && filter.Flyout.IsOpen, "genre loading preserves nested popup");
            genreChoice.SelectedItem = genreChoice.ItemsSource!.Cast<FilterChoice>().First(choice => choice.Value == "Pop");
            genreChoice.IsDropDownOpen = false; Wait(() => !model.IsLoading); Check(model.Filters.Genre == "Pop", "genre choice query");
            Capture("songs-filter-light.png"); filter.Flyout.Hide();
            model.Filters.Reset(); Wait(() => !model.IsLoading);
            var sorting = view.GetVisualDescendants().OfType<SongSortControl>().Single();
            var sortButton = sorting.FindControl<Button>("SortButton")!; Click(sortButton); Pump();
            model.IsMultiSortEnabled = true; model.SelectSort((int)TrackSort.Artist, false); model.SelectSort((int)TrackSort.Album, true);
            Wait(() => !model.IsLoading); Check(model.SecondaryIndex > 0 && model.TertiaryIndex > 0, "three sort priorities");
            Capture("songs-sort-light.png"); sortButton.Flyout!.Hide();
            model.IsMultiSortEnabled = false; model.SelectSort((int)TrackSort.Title); Wait(() => !model.IsLoading);
            var activeCover = view.GetVisualDescendants().OfType<SongTrackRow>().First().FindControl<Image>("Cover")!;
            var imageBeforeTheme = activeCover.Source;
            shell.IsDark = true; Pump(); Check(ReferenceEquals(activeCover.Source, imageBeforeTheme), "artwork survives theme change"); Capture("songs-dark.png"); Click(filter); Pump(); Capture("songs-filter-dark.png"); filter.Flyout.Hide();
            window.Width = 900; window.Height = 780; shell.IsDark = false; Pump(); Capture("songs-narrow.png");
            shell.NavigateCommand.Execute("home"); Pump();
            Check(model.Items.Count == 0, "page resources released");
            Console.WriteLine("Songs native preview: row virtualization, cover play (simulated audio), persistent nested filter, three sort priorities, light/dark, narrow window and release passed.");
        }
        finally
        {
            window.ReleaseLibraryPage(); window.RequestExit(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization();
            Complete(session.DisposeAsync().AsTask()); Complete(artwork.DisposeAsync().AsTask()); Complete(db.DisposeAsync().AsTask());
        }
        void Capture(string name)
        {
            Pump(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96,96));
            bitmap.Render(window); bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
        void Click(Control control)
        {
            window.UpdateLayout(); var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
        }
    }
    private static void Seed(LibraryDatabase db, string root)
    {
        var titles = new[] { "Afterglow", "Bloom", "City Lights", "Dawn", "Flower Dance", "Grace", "Home", "Ref:rain",
            "Sleeping Butterfly", "Telephone (Piano Arrange)", "七里香", "安静", "晴天", "夜曲", "群青", "花海", "小情歌", "想自由" };
        var artists = new[] { "Aimer", "Spangle Call Lilli Line", "Nujabes", "周杰伦", "苏打绿", "林宥嘉" };
        var albums = new[] { "Penny Rain", "Piano Lesson", "Modal Soul", "十一月的萧邦", "小宇宙", "美妙生活" };
        var job = db.BeginScanAsync(root, false, CancellationToken.None); Complete(job);
        var rows = Enumerable.Range(0, 218).Select(index =>
        {
            var group = index % 6; var folder = Path.Combine(root,"music", group.ToString()); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{index:000}.flac"); File.WriteAllBytes(path, "fLaC"u8.ToArray());
            var cover = Path.Combine(folder, "cover.png");
            if (!File.Exists(cover) && !File.Exists(Path.Combine(folder, "cover.webp")))
            {
                var source = group < 4 ? Path.Combine("misc", "queue-rework", "media", "album-" + group, "cover.png") : "misc/lyrics-fidelity/reference/cover.webp";
                if (File.Exists(source)) File.Copy(source, group < 4 ? cover : Path.Combine(folder, "cover.webp"));
            }
            var title = (index < 18 ? titles[index] : $"Session {index / 18:00} · {titles[index % titles.Length]}");
            return new ScanEntry(new(path, LibraryPath.Key(path), 100, 1000 + index), ScanEntryKind.Metadata,
                new(title, artists[group], albums[group], artists[group], 180 + index % 100, "FLAC", index % 2 == 0 ? 96000 : 44100, index % 2 == 0 ? 24 : 16, Year: 2026, Genre: "Pop"));
        }).ToArray();
        long sequence = 0; foreach (var batch in rows.Chunk(128)) Complete(db.CommitScanBatchAsync(job.Result, ++sequence, batch, CancellationToken.None));
        Complete(db.FinishScanAsync(job.Result, ScanStatus.Completed, rows.Length, false, null, CancellationToken.None));
    }
    private static void Complete(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew(); while (!condition()) { if (watch.Elapsed.TotalSeconds > 6) throw new TimeoutException("Songs preview wait."); Pump(); Thread.Sleep(5); } Pump();
    }
    private sealed class CaptureNative : INativeAudio
    {
        private int operation;
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            var response = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openFile" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":255}}",
                _ => "true"
            };
            using var document = JsonDocument.Parse(response); return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
