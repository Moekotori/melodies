using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Genres;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playback;
using Echo.Playback.NativeAudio;
using Microsoft.Data.Sqlite;

// Isolated new-format database, native Avalonia render, simulated audio acknowledgements.
internal static class GenreUiSmoke
{
    public static void Run(string output, string? artworkDirectory)
    {
        Directory.CreateDirectory(output);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var reuse = Environment.GetEnvironmentVariable("ECHO_GENRE_UI_ONLY") == "1";
        var path = reuse ? Directory.EnumerateFiles(output, "genres-*.sqlite").First(file => !file.Contains(".before-", StringComparison.Ordinal)) : Path.Combine(output, "genres-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var database = Complete(LibraryDatabase.OpenAsync(path, budget.Token));
        var runtime = new Echo.Application.Playback.PlaybackRuntime(new PreviewNative());
        MainWindow? window = null;
        try
        {
            if (!reuse)
            {
            string[] genres = ["Anime", "J-Pop", "Game", "Soundtrack", "JPop", "Pop", "Soundtracks", "Blues", "VTuber",
                "Rock", "Prog Rock", "VOCALOID", "Other", "Classical", "Alternative", "Hip-Hop/Rap", "Hard Rock", "Instrumental",
                "アニメ", "Electronic", "Indie Rock", "Stage", "Animation", "流行歌曲演唱", "Alternative & Indie", "Films/Games", "Pop/Rock"];
            var covers = artworkDirectory is not null && Directory.Exists(artworkDirectory)
                ? Directory.EnumerateFiles(artworkDirectory, "*.png").Take(27).ToArray() : [];
            var root = Path.Combine(output, "media"); var entries = new List<ScanEntry>();
            for (var genre = 0; genre < 650; genre++)
            {
                var name = genre < genres.Length ? genres[genre] : $"Genre {genre:0000}";
                var count = genre == 0 ? 241 : genre < 27 ? 55 - genre : 1;
                for (var track = 0; track < count; track++)
                {
                    var album = $"{name} · Collection {track % 4 + 1}";
                    var directory = Path.Combine(root, $"genre-{genre:0000}", $"album-{track % 4}"); Directory.CreateDirectory(directory);
                    if (genre < covers.Length && !File.Exists(Path.Combine(directory, "cover.png"))) File.Copy(covers[genre], Path.Combine(directory, "cover.png"));
                    var audio = Path.Combine(directory, $"{track:0000}.flac"); File.WriteAllBytes(audio, []);
                    entries.Add(Entry(audio, name, album, track + 1));
                }
            }
            var normalized = Path.Combine(root, "normalized.flac");
            entries.Add(Entry(normalized, " ＡＮＩＭＥ ", "Normalization", 1));
            entries.Add(Entry(Path.Combine(root, "untagged.flac"), " ", "Untagged", 1));
            var job = Complete(database.BeginScanAsync(root, false, budget.Token)); long batch = 0;
            foreach (var chunk in entries.Chunk(200)) Complete(database.CommitScanBatchAsync(job, ++batch, chunk, budget.Token));
            Complete(database.FinishScanAsync(job, ScanStatus.Completed, entries.Count, false, null, budget.Token));
            var first = Complete(database.GetGenresAsync(new(), budget.Token));
            var anime = first.Items[0]; Require(anime.Id == "anime" && anime.TrackCount == 242 && anime.AlbumCount == 5, "normalization / aggregate counts");
            Require(first.Total == 651 && first.Items.Count == 200, "bounded first page including unclassified");
            var second = Complete(database.GetGenresAsync(new(200, Revision: first.Revision), budget.Token));
            Require(!first.Items.Select(row => row.Id).Intersect(second.Items.Select(row => row.Id)).Any(), "stable genre pages");
            Require(Complete(database.GetGenresAsync(new(Search: "ＰＯＰ/ＲＯＣＫ"), budget.Token)).Items.Single().Id == "pop/rock", "whole genre tag semantics");
            Require(Complete(database.GetGenresAsync(new(Search: "%"), budget.Token)).Total == 0, "literal LIKE escaping");
            Require(Complete(database.GetGenreAlbumsAsync(anime.Id, new(), budget.Token)).Total == 5, "genre album relation");
            var tracks = Complete(database.GetGenreTracksAsync(new(anime.Id), budget.Token));
            Require(tracks.Items.Count == 200 && Complete(database.GetGenreTracksAsync(new(anime.Id, 200), budget.Token)).Items.Count == 42, "bounded genre tracks");
            using (var cancelled = new CancellationTokenSource())
            { cancelled.Cancel(); try { Complete(database.GetGenresAsync(new(), cancelled.Token)); throw new Exception("ignored cancellation"); } catch (OperationCanceledException) { } }

            // Change the last track of its album to a new genre, then mark the root missing and recover it.
            var edit = Complete(database.BeginScanAsync(root, false, budget.Token));
            Complete(database.CommitScanBatchAsync(edit, 1, [Entry(normalized, "New Genre", "Moved album", 1, 2)], budget.Token));
            Complete(database.FinishScanAsync(edit, ScanStatus.Completed, 1, false, null, budget.Token));
            var updated = Complete(database.GetGenresAsync(new(Search: "Anime"), budget.Token)).Items.Single();
            Require(updated.TrackCount == 241 && updated.AlbumCount == 4, "retag / album count maintenance");
            try { Complete(database.GetGenresAsync(new(200, Revision: first.Revision), budget.Token)); throw new Exception("stale revision accepted"); } catch (LibraryChangedException) { }
            var missing = Complete(database.BeginScanAsync(root, true, budget.Token));
            Complete(database.FinishScanAsync(missing, ScanStatus.Completed, 0, true, null, budget.Token));
            Require(Complete(database.GetGenresAsync(new(), budget.Token)).Total == 0, "missing removes genre facts");
            var recover = Complete(database.BeginScanAsync(root, false, budget.Token)); batch = 0;
            foreach (var chunk in entries.Chunk(200)) Complete(database.CommitScanBatchAsync(recover, ++batch, chunk, budget.Token));
            Complete(database.FinishScanAsync(recover, ScanStatus.Completed, entries.Count, false, null, budget.Token));
            Require(Complete(database.GetGenresAsync(new(), budget.Token)).Total == 651, "restoration recreates genre facts");
            Complete(database.DisposeAsync().AsTask());
            // Model an existing v12 fixture: only remove this feature's derived schema after closing the owner.
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open(); using var command = connection.CreateCommand();
                command.CommandText = "DROP TRIGGER tracks_genre_insert; DROP TRIGGER tracks_genre_update; DROP TRIGGER tracks_genre_delete; DROP TABLE genre_tracks; DROP TABLE genre_albums; DROP TABLE genres; DROP TRIGGER IF EXISTS albums_browse_insert; DROP TRIGGER IF EXISTS tracks_browse_insert; DROP TRIGGER IF EXISTS tracks_browse_delete; DROP TRIGGER IF EXISTS tracks_browse_update; DROP TABLE IF EXISTS history_search; DROP TABLE IF EXISTS history_state; DROP TABLE listening_events; CREATE TABLE listening_events(id TEXT PRIMARY KEY,track_id TEXT NOT NULL,started INTEGER NOT NULL,updated INTEGER NOT NULL,seconds REAL NOT NULL CHECK(seconds>=0)); CREATE INDEX listening_started ON listening_events(started DESC,track_id); CREATE INDEX listening_track ON listening_events(track_id,started DESC); PRAGMA user_version=12;";
                command.ExecuteNonQuery();
            }
            database = Complete(LibraryDatabase.OpenAsync(path, budget.Token));
            Require(Complete(database.GetGenresAsync(new(), budget.Token)).Total == 651, "v12 backfill");
            Require(Directory.EnumerateFiles(output, "*.before-v13-*.sqlite").Any(), "consistent pre-migration backup");

            }
            window = DesktopComposition.CreateWindow(false, false, database, playback: runtime);
            var shell = (ShellViewModel)window.DataContext!; shell.Text.SetLanguage("zh-CN");
            shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
            window.Show(); Pump(); shell.NavigateCommand.Execute("genres"); Pump();
            var view = window.GetVisualDescendants().OfType<GenresView>().Single();
            Wait(() => !view.Model.IsBusy && view.Viewport.RealizedCards > 0, "genre startup");
            Wait(() => view.GetVisualDescendants().OfType<GenreCard>().All(card => card.HasArtworkResult), "artwork settlement");
            Capture(window, output, "genres-light"); shell.IsDark = true; Pump(); Capture(window, output, "genres-dark");
            var button = view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("genre-card") && b.IsEnabled);
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.End });
            Wait(() => view.Model.At(650) is not null && window.FocusManager?.GetFocusedElement() is Button b && b.TabIndex == 650, "keyboard deep paging");
            Require(view.Model.ResidentGenres <= 600 && view.Viewport.RealizedCards <= 84, "DTO/card bounds");
            window.Hide(); Pump(); Require(view.Viewport.RealizedCards == 0, "hidden resource release");
            window.Show(); Wait(() => view.Viewport.RealizedCards > 0, "visible restore");
            view.Model.Search = "stale-search"; view.Model.Search = "Anime";
            Wait(() => !view.Model.IsBusy && view.Model.Total == 1, "latest search");
            view.Model.Search = "impossible-genre"; Wait(() => view.Model.IsEmpty, "empty search state");
            view.Model.Search = "Anime"; Wait(() => !view.Model.IsBusy && view.Model.Total == 1, "detail source");
            button = view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("genre-card") && b.IsEnabled);
            Click(button); Wait(() => view.GetVisualDescendants().OfType<GenreDetailView>().Any(), "open genre detail");
            var detail = view.GetVisualDescendants().OfType<GenreDetailView>().Single();
            var model = (GenreDetailViewModel)detail.DataContext!;
            Wait(() => !model.IsLoading && model.Tracks.Count == 200 && detail.GetVisualDescendants().OfType<AlbumCard>().Any(), "detail albums and tracks");
            Require(view.Viewport.RealizedCards == 0 && model.IsAlbums, "default album tab releases genre wall");
            Capture(window, output, "genre-detail-dark");
            var albumButton = detail.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("album-card") && b.IsEnabled);
            Click(albumButton); Wait(() => view.GetVisualDescendants().OfType<AlbumDetailView>().Any(), "open album");
            var albumDetail = view.GetVisualDescendants().OfType<AlbumDetailView>().Single();
            Click(albumDetail.GetVisualDescendants().OfType<Button>().First(b => b.Content is string value && value.Contains("返回")));
            Wait(() => view.GetVisualDescendants().OfType<GenreDetailView>().Any(), "return to genre");
            detail = view.GetVisualDescendants().OfType<GenreDetailView>().Single(); model = (GenreDetailViewModel)detail.DataContext!;
            Wait(() => !model.IsLoading && model.Tracks.Count == 200, "returned tracks");
            Click(detail.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "0")));
            Require(model.IsOverview, "track tab"); Complete(model.NextCommand.ExecuteAsync(null)); Require(model.Tracks.Count == 42, "track paging");
            Complete(model.PlayTrackAsync(model.Tracks[0].Row)); Require(shell.Player.Session.Snapshot.Current?.TrackId == model.Tracks[0].Row.Id, "track playback");
            Complete(model.AddTrackAsync(model.Tracks[1].Row, true)); Require(shell.Player.Session.Queue.Count == 2, "play next");
            Complete(model.AddToQueueCommand.ExecuteAsync(null)); Require(shell.Player.Session.Queue.Count == 244, "genre enqueue");
            Capture(window, output, "genre-tracks-dark");
            shell.Text.SetLanguage("en-US"); Pump(); Require(model.Meta.Contains("tracks"), "live localization");
            shell.Text.SetLanguage("zh-CN"); shell.IsDark = false; Pump();
            Click(detail.GetVisualDescendants().OfType<Button>().First(b => b.Content is string value && value.Contains("返回流派")));
            Wait(() => view.Viewport.RealizedCards > 0, "return to wall"); view.Model.Search = ""; Wait(() => !view.Model.IsBusy && view.Model.Total == 651, "search reset");
            window.Width = 800; Pump(); Capture(window, output, "genres-narrow");
            shell.NavigateCommand.Execute("home"); Pump(); Require(view.Model.ResidentGenres == 0 && view.Viewport.RealizedCards == 0, "navigation disposal");
            Console.WriteLine(reuse ? "PASS UI: native wall/detail, bounded paging, keyboard, simulated playback/queue, localization, narrow layout and disposal." : "PASS: genre normalization, album/track counts, stable bounded paging, cancellation, revision rejection, retag/missing/restoration, v12 backup/backfill, native Avalonia wall/detail navigation, queue, localization, keyboard, narrow layout and resource disposal. Audio acknowledgements simulated.");
        }
        finally
        { window?.Close(); Pump(); Complete(runtime.DisposeAsync().AsTask()); Complete(database.DisposeAsync().AsTask()); }
    }
    private static ScanEntry Entry(string path, string genre, string album, int number, long ticks = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllBytes(path, []);
        return new(new(path, LibraryPath.Key(path), 1_000_000, ticks), ScanEntryKind.Metadata,
            new($"Track {number:000}", "ECHO Ensemble", album, "ECHO Ensemble", 180 + number, "flac", 96000, 24, number, Genre: genre));
    }
    private static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    private static T Complete<T>(Task<T> task) { Wait(() => task.IsCompleted, "async operation"); return task.GetAwaiter().GetResult(); }
    private static void Complete(Task task) { Wait(() => task.IsCompleted, "async operation"); task.GetAwaiter().GetResult(); }
    private static void Wait(Func<bool> ready, string name)
    { var watch = Stopwatch.StartNew(); while (!ready()) { if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException(name); Pump(); } Pump(); }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string output, string name)
    { Pump(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96)); bitmap.Render(window); bitmap.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default); }
    private sealed class PreviewNative : INativeAudio
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
                "audio.openFile" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":223}}", _ => "true"
            };
            using var document = JsonDocument.Parse(value); return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
