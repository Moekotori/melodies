using System.Diagnostics;
using System.Text.Json;
using Echo.Playback;
using Echo.Playback.NativeAudio;
using Echo.Desktop.Features.Player;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Search;
using Echo.Desktop.Features.Songs;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Library;

internal static class LibrarySearchUiSmoke
{
    public static void Run(string databasePath, string output)
    {
        Directory.CreateDirectory(output);
        var opening = LibraryDatabase.OpenAsync(databasePath); Wait(() => opening.IsCompleted);
        var db = opening.GetAwaiter().GetResult();
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork());
        var shell = new ShellViewModel(); shell.AttachLibrary(db, artwork);
        var session = new PlaybackSessionCoordinator(new SearchNative());
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session), shell.Text); shell.AttachPlayback(player);
        shell.Text.SetLanguage("zh-CN");
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        try
        {
            window.Show(); shell.NavigateCommand.Execute("songs");
            var view = window.GetVisualDescendants().OfType<SongsView>().Single(); var model = view.Model!;
            Wait(() => !model.IsLoading && model.Items.Count > 0);
            Require(model.Items.Count == 200, "bounded first page");
            model.Selected = model.Items[4];
            var play = model.PlayCommand.ExecuteAsync(null); Wait(() => play.IsCompleted); play.GetAwaiter().GetResult();
            Require(session.Queue.Count == 200 && session.Snapshot.Current?.TrackId == model.Selected.Id, "bounded page play and selected start");
            model.Selected = model.Items[5]; play = model.PlayNextCommand.ExecuteAsync(null); Wait(() => play.IsCompleted); play.GetAwaiter().GetResult();
            Require(session.Queue.Any(track => track.QueuedNext && track.TrackId == model.Selected.Id), "next-up action");
            var next = model.NextCommand.ExecuteAsync(null); Wait(() => next.IsCompleted); next.GetAwaiter().GetResult();
            Require(model.Items.Count == 200 && model.CanPrevious, "next page");
            model.Search = "/artist 周杰伦"; Wait(() => !model.IsLoading);
            Require(model.Items.Count == 2, "bound command search");
            model.Search = ""; model.SortIndex = 1; model.SecondaryIndex = 3;
            Wait(() => !model.IsLoading && model.Items.Count == 200);
            model.Filters.RateIndex = 4; model.Filters.DepthIndex = 2;
            Wait(() => !model.IsLoading);
            Require(model.Items.All(track => track.SampleRate == 96000 && track.BitDepth == 24), "bound combined filters");
            var filter = view.FindControl<Button>("FilterButton")!; Click(filter);
            Require(filter.Flyout!.IsOpen, "filter opened");
            Capture("songs-filters-light.png");
            shell.IsDark = true; Capture("songs-filters-dark.png"); filter.Flyout?.Hide();
            shell.NavigateCommand.Execute("albums");
            var albums = window.GetVisualDescendants().OfType<AlbumsView>().Single();
            Wait(() => !albums.Model.IsLoading && albums.Model.Revision is not null);
            albums.Model.Filters.Year = "2026"; Wait(() => !albums.Model.IsLoading);
            Require(albums.Model.Total == 1, "shared album filters");
            shell.NavigateCommand.Execute("songs");
            view = window.GetVisualDescendants().OfType<SongsView>().Single(); model = view.Model!;
            Wait(() => !model.IsLoading && model.Items.Count > 0);
            Require(model.SortIndex == 1 && model.SecondaryIndex == 3 && model.Filters.RateIndex == 4, "query preference navigation restore");
            model.Search = "thiswillnotmatch"; model.Search = "旅行";
            Wait(() => !model.IsLoading); Require(model.Items.Count == 1, "late query suppression");
            Click(window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "GlobalSearchButton"));
            Require(shell.IsGlobalSearchOpen, "titlebar global search entry");
            var overlay = window.GetVisualDescendants().OfType<GlobalSearchOverlay>().Single();
            overlay.Model!.Search = "Aurora"; Wait(() => !overlay.Model.IsLoading);
            Require(overlay.Model.Hits.Count <= 24 && overlay.Model.Hits.Select(hit => hit.Target.GetType()).Distinct().Count() == 3, "bounded global overlay categories");
            shell.IsDark = false; Capture("global-search-overlay-light.png");
            shell.CloseGlobalSearchCommand.Execute(null);
            shell.NavigateCommand.Execute("search");
            var search = window.GetVisualDescendants().OfType<LibrarySearchView>().Single();
            search.Model!.Search = "Aurora"; Wait(() => !search.Model.IsLoading);
            Require(search.Model.Hits.Select(hit => hit.Target.GetType()).Distinct().Count() == 3 && search.Model.Hits.Count <= 90, "global three categories");
            shell.IsDark = false; Capture("global-search-light.png");
            shell.Text.SetLanguage("en-US"); Capture("global-search-en.png");
            shell.NavigateCommand.Execute("home");
            Require(model.Items.Count == 0 && search.Model.Hits.Count == 0, "navigation cancellation and release");
            Console.WriteLine("Native Avalonia: bounded paging, bound command search, shared filters, multi-sort, navigation restore, rapid-query cancellation, three-category search and release passed.");
        }
        finally
        {
            window.ReleaseLibraryPage(); window.Close(); player.Detach();
            var stopping = session.DisposeAsync().AsTask(); Wait(() => stopping.IsCompleted); stopping.GetAwaiter().GetResult();
            shell.Settings.Dispose(); shell.ReleaseLocalization();
            var close = artwork.DisposeAsync().AsTask(); Wait(() => close.IsCompleted); close.GetAwaiter().GetResult();
            close = db.DisposeAsync().AsTask(); Wait(() => close.IsCompleted); close.GetAwaiter().GetResult();
        }
        void Capture(string name)
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            using var bitmap = new RenderTargetBitmap(new PixelSize(1440, 900), new Vector(96, 96)); bitmap.Render(window);
            bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
        void Click(Button button)
        {
            window.UpdateLayout(); var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        }
    }
    private sealed class SearchNative : INativeAudio
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
    private static void Require(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
    private static void Wait(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed.TotalSeconds > 8) throw new TimeoutException("Search UI wait exceeded 8 seconds."); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Dispatcher.UIThread.RunJobs();
    }
}
