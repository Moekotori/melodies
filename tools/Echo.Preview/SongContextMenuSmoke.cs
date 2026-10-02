using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Songs;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playback;

internal static class SongContextMenuSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output); var timer = Stopwatch.StartNew();
        MainWindow? window = null;
        var opening = LibraryDatabase.OpenAsync(Path.Combine(output, "menu.sqlite")); Complete(opening); var db = opening.Result;
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork());
        var shell = new ShellViewModel(); shell.AttachLibrary(db, artwork); shell.Text.SetLanguage("zh-CN");
        var session = new PlaybackSessionCoordinator(new MenuPreviewAudio());
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session), shell.Text); shell.AttachPlayback(player);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var root = Path.Combine(output, "media"); Directory.CreateDirectory(root);
        var begin = db.BeginScanAsync(root, false, CancellationToken.None); Complete(begin);
        var rows = Enumerable.Range(0, 24).Select(index =>
        {
            var path = Path.Combine(root, $"{index:00}.flac"); File.WriteAllBytes(path, "fLaC"u8.ToArray());
            return new ScanEntry(new(path, LibraryPath.Key(path), 4, 1000 + index), ScanEntryKind.Metadata,
                new($"曲目 {index:00}", "Aimer", "Penny Rain", "Aimer", 200, "FLAC", 96000, 24, Genre: "Pop"));
        }).ToArray();
        Complete(db.CommitScanBatchAsync(begin.Result, 1, rows, CancellationToken.None));
        Complete(db.FinishScanAsync(begin.Result, ScanStatus.Completed, 24, false, null, CancellationToken.None));
        for (var i = 0; i < 21; i++)
        { var creating = db.CreatePlaylistAsync(Guid.NewGuid().ToString("N"), $"歌单 {i:00}", ""); Complete(creating); }
        window = new MainWindow { DataContext = shell, Width = 1280, Height = 900 };
        try
        {
            window.Show(); shell.NavigateCommand.Execute("songs");
            var view = window.GetVisualDescendants().OfType<SongsView>().Single(); var model = view.Model!;
            Until(() => !model.IsLoading && model.Items.Count == 24);
            var target = model.Items[1]; Open(target);
            Require(model.Selected?.Id == target.Id, "right click selects the pointed row");
            SaveMenu("song-menu-light.png"); shell.IsDark = true; Settle(); SaveMenu("song-menu-dark.png"); shell.IsDark = false;
            var playlistMenu = Item("SongAddToPlaylist"); playlistMenu.IsSubMenuOpen = true;
            Until(() => playlistMenu.Items.OfType<MenuItem>().Count(item => item.Name == "SongPlaylistTarget") == 20);
            Require(playlistMenu.Items.Count <= 25, "bounded playlist submenu");
            SaveScene("song-playlist-submenu.png");
            Click(playlistMenu.Items.OfType<MenuItem>().Single(item => item.Header as string == "下一页"));
            Until(() => playlistMenu.Items.OfType<MenuItem>().Count(item => item.Name == "SongPlaylistTarget") == 1);
            var playlistItem = playlistMenu.Items.OfType<MenuItem>().First(item => item.Name == "SongPlaylistTarget");
            var playlistName = playlistItem.Header as string;
            Click(playlistItem); Until(() => !model.IsActing);
            var allLists = db.GetPlaylistsAsync(new(Limit: 200)); Complete(allLists);
            var playlistTracks = db.GetPlaylistTracksAsync(new(allLists.Result.Items.Single(item => item.Name == playlistName).Id)); Complete(playlistTracks);
            Require(playlistTracks.Result.Items.Single().Track.Id == target.Id, "playlist writes the pointed track");
            Open(target); Click(Item("SongMenuAppend")); Until(() => session.Queue.Count == 1 && !model.IsActing);
            Require(session.Queue[0].TrackId == target.Id, "append uses the pointed track");
            Open(target); Click(Item("SongMenuFavorite")); Until(() => !model.IsActing && !model.IsLoading);
            target = model.Items.Single(item => item.Id == target.Id); Require(target.Favorite, "favorite persisted");
            Open(target); Require(Item("SongMenuFavorite").Header as string == "取消收藏", "favorite label reflects state");
            Click(Item("SongMenuFavorite")); Until(() => !model.IsActing && !model.IsLoading);
            target = model.Items.Single(item => item.Id == target.Id); Require(!target.Favorite, "unfavorite persisted");
            Open(target); var info = Item("SongMenuInfo"); Click(info);
            Until(() => window.OwnedWindows.Count > 0);
            var dialog = window.OwnedWindows.Single(); Capture(dialog, "song-information.png"); dialog.Close(); Until(() => !model.IsActing);
            Open(target); var copy = Item("SongMenuCopy");
            if (copy.IsEnabled)
            {
                Click(copy); Until(() => !model.IsActing);
                var value = window.Clipboard!.TryGetValueAsync(DataFormat.Text); Complete(value);
                Require(value.Result == target.Path, "copy returns the physical path");
            }
            else view.FindControl<ListBox>("TrackList")!.ContextMenu!.Close();
            model.Selected = target; view.FindControl<ListBox>("TrackList")!.Focus();
            window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null); Settle();
            Require(view.FindControl<ListBox>("TrackList")!.ContextMenu!.IsOpen, "Shift F10 opens the same menu");
            view.FindControl<ListBox>("TrackList")!.ContextMenu!.Close();
            shell.Text.SetLanguage("en-US"); Open(target); SaveMenu("song-menu-english.png");
            shell.NavigateCommand.Execute("home"); Settle(); Require(model.Items.Count == 0, "navigation closes menu and releases the page");
            Console.WriteLine("Native menu: right click, 20-item playlist paging, playlist write, queue append, favorite/unfavorite, information dialog, clipboard capability, keyboard entry, languages and navigation release passed. Audio is simulated; OS folder opening was not invoked.");

            void Open(TrackRow track)
            {
                view.FindControl<ListBox>("TrackList")!.ContextMenu!.Close(); Settle();
                var row = view.GetVisualDescendants().OfType<SongTrackRow>().First(item => item.DataContext is TrackRow value && value.Id == track.Id);
                var point = row.TranslatePoint(new Point(80, 28), window)!.Value;
                window.MouseMove(point); window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Settle();
                Require(view.FindControl<ListBox>("TrackList")!.ContextMenu!.IsOpen, "right-click menu opens");
            }
            MenuItem Item(string name) => view.FindControl<ListBox>("TrackList")!.ContextMenu!.Items.OfType<MenuItem>().Single(item => item.Name == name);
            void SaveMenu(string name)
            {
                Settle(); var menu = view.FindControl<ListBox>("TrackList")!.ContextMenu!;
                using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(menu.Bounds.Width), (int)Math.Ceiling(menu.Bounds.Height)), new Vector(96, 96));
                image.Render(menu); image.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window?.ReleaseLibraryPage(); window?.RequestExit(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization();
            Complete(session.DisposeAsync().AsTask()); Complete(artwork.DisposeAsync().AsTask()); Complete(db.DisposeAsync().AsTask());
        }
        void Click(Control control)
        {
            Settle(); var top = TopLevel.GetTopLevel(control)!;
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
            top.MouseMove(point); top.MouseDown(point, MouseButton.Left); top.MouseUp(point, MouseButton.Left); Settle();
        }
        void Capture(TopLevel top, string name)
        { Settle(); using var frame = top.CaptureRenderedFrame() ?? throw new IOException("Menu capture missing"); frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); }
        void SaveScene(string name) => Capture(window!, name);
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Settle() { Dispatcher.UIThread.RunJobs(); window?.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Until(Func<bool> condition)
        { while (!condition()) { if (timer.Elapsed.TotalSeconds > 25) throw new TimeoutException("Song menu UI exceeded 25 seconds."); Settle(); Thread.Sleep(2); } Settle(); }
        void Complete(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    }
    private sealed class MenuPreviewAudio : INativeAudio
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
                "audio.openFile" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":255}}",
                _ => "true"
            };
            using var document = JsonDocument.Parse(value); return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
