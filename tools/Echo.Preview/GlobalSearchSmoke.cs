using System.Diagnostics;
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
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Playlists;
using Echo.Desktop.Features.Search;
using Echo.Desktop.Features.Settings.Models;
using Echo.Desktop.Features.Songs;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playback;

internal static class GlobalSearchSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var clock = Stopwatch.StartNew();
        var opening = LibraryDatabase.OpenAsync(Path.Combine(output, "search.sqlite")); Wait(opening);
        var database = opening.Result;
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork());
        var session = new PlaybackSessionCoordinator(new QueueUiSmoke.PreviewNative());
        var shell = new ShellViewModel();
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session), shell.Text);
        shell.AttachPlayback(player); shell.AttachLibrary(database, artwork);
        shell.Text.SetLanguage("zh-CN");
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        try
        {
            Seed(); window.Show(); shell.NavigateCommand.Execute("songs"); Pump();
            var songs = window.GetVisualDescendants().OfType<SongsView>().Single();
            Until(() => !songs.Model!.IsLoading && songs.Model.Items.Count > 0);
            var pageSearch = songs.FindControl<TextBox>("SearchField")!; pageSearch.Focus();
            var overlay = window.FindControl<GlobalSearchOverlay>("GlobalSearchOverlay")!;
            window.KeyPress(Key.F4, RawInputModifiers.None, PhysicalKey.F4, null); Pump();
            Require(shell.IsGlobalSearchOpen && overlay.Model is not null && shell.ActiveRouteId == "songs", "F4 keeps the current page");
            window.KeyPress(Key.F4, RawInputModifiers.None, PhysicalKey.F4, null); Pump();
            Require(shell.IsGlobalSearchOpen, "F4 auto-repeat ignored"); window.KeyRelease(Key.F4, RawInputModifiers.None, PhysicalKey.F4, null);
            var field = overlay.FindControl<TextBox>("SearchField")!;
            Require(field.IsFocused, "automatic search focus from a text field");
            Require(overlay.Model!.Hits.Count > 0 && !overlay.Model.IsLoading, "empty field shows page shortcuts without library reads");
            Capture("empty-light.png");
            var current = overlay.Model!; current.Search = "Aurora"; Until(() => !current.IsLoading);
            Require(current.Hits.Count <= 25 && current.Hits.Any(hit => hit.Target is TrackRow) && current.Hits.Any(hit => hit.Target is AlbumRow) && current.Hits.Any(hit => hit.Target is ArtistRow), "bounded mixed library results");
            var list = overlay.FindControl<ListBox>("HitList")!;
            Press(Key.Down); Require(list.SelectedIndex == 1 && field.IsFocused, "arrow selection keeps typing focus");
            Capture("results-light.png"); shell.IsDark = true; Capture("results-dark.png");
            Press(Key.Escape); Require(!shell.IsGlobalSearchOpen && overlay.Model is null && pageSearch.IsFocused && current.Hits.Count == 0, "Esc releases results and restores focus");
            Press(Key.F4); current = overlay.Model!; current.Search = "Aurora"; Until(() => !current.IsLoading);
            list.SelectedItem = current.Hits.First(hit => hit.Target is AlbumRow); Press(Key.Enter);
            Require(shell.ActiveRouteId == "albums" && !shell.IsGlobalSearchOpen && window.GetVisualDescendants().OfType<AlbumDetailView>().Any(), "album result opens full detail page");
            Press(Key.F4); current = overlay.Model!; current.Search = "Aurora"; Until(() => !current.IsLoading);
            list.SelectedItem = current.Hits.First(hit => hit.Target is ArtistRow); Press(Key.Enter);
            Require(shell.ActiveRouteId == "artists" && window.GetVisualDescendants().OfType<ArtistDetailView>().Any(), "artist result opens full detail page");
            Press(Key.F4); current = overlay.Model!; current.Search = "语言"; Until(() => !current.IsLoading);
            list.SelectedItem = current.Hits.First(hit => hit.Target is SettingsSearchResult { RowId: "language" }); Press(Key.Enter);
            Require(shell.ActiveRouteId == "settings" && shell.Settings.Sections.SelectMany(section => section.Rows).Any(row => row.Id == "language" && row.IsHighlighted), "setting result opens and highlights row");
            Press(Key.F4); current = overlay.Model!; current.Search = "Night Drive"; Until(() => !current.IsLoading);
            list.SelectedItem = current.Hits.First(hit => hit.Target is PlaylistRow); Press(Key.Enter);
            var playlistView = window.GetVisualDescendants().OfType<PlaylistsView>().Single();
            Until(() => !playlistView.Model.IsLoading && playlistView.Model.Tracks.Count == 1);
            Require(shell.ActiveRouteId == "playlists" && playlistView.Model.SelectedPlaylist?.Name == "Night Drive", "playlist result opens its tracks");
            Press(Key.F4); current = overlay.Model!; current.Search = "曲目"; Until(() => !current.IsLoading);
            list.SelectedItem = current.Hits.First(hit => hit.Target is SearchPageTarget { Route: "songs" }); Press(Key.Enter);
            Require(shell.ActiveRouteId == "songs", "page result navigation");
            Press(Key.F4); current = overlay.Model!; current.Search = "Aurora 01"; Until(() => !current.IsLoading);
            var songItem = list.GetVisualDescendants().OfType<ListBoxItem>().First(item => item.DataContext is GlobalSearchHit { Target: TrackRow });
            var point = songItem.TranslatePoint(new Point(80, songItem.Bounds.Height - 18), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
            var locatedSongs = window.GetVisualDescendants().OfType<SongsView>().Single();
            Until(() => locatedSongs.Model!.Selected?.Title == "Aurora 01");
            Require(!shell.IsGlobalSearchOpen && session.Snapshot.Current is null, "click locates song without starting playback");
            Press(Key.F4); current = overlay.Model!; Until(() => !current.IsLoading);
            list.SelectedItem = current.Hits.First(hit => hit.Target is TrackRow); Press(Key.Enter);
            Until(() => !shell.IsGlobalSearchOpen);
            Require(session.Snapshot.Current?.Metadata?.GetValueOrDefault("title") == "Aurora 01", "track result starts playback through existing session");
            Press(Key.F4); current = overlay.Model!;
            Require(current.Search == "Aurora 01", "reopening preserves query");
            current.Search = "";
            Require(current.Hits.Any(hit => hit.SectionTitle == current.Copy.Recent) && current.Hits.Count <= 13, "bounded recent destinations and pages");
            current.Search = "old-does-not-exist"; current.Search = "Aurora"; Until(() => !current.IsLoading);
            Require(current.Hits.Any(hit => hit.Target is TrackRow), "rapid query suppresses stale results");
            current.Search = "pending"; Press(Key.F4); Require(overlay.Model is null && current.Hits.Count == 0, "closing cancels pending work");
            shell.OpenSearchCommand.Execute(null); current = overlay.Model!; current.Search = "media"; Until(() => !current.IsLoading);
            Require(current.Hits.Any(hit => hit.Target is FolderRow), "folder search uses library workbench query");
            current.Search = "missing-keyword"; Until(() => !current.IsLoading);
            Require(current.IsEmpty, "no matches state"); Capture("no-results-dark.png");
            Require(current.Hits.Single().Target is SearchQueryTarget { Route: "settings" }, "no-match settings search remains available");
            current.Search = "Aurora"; Until(() => !current.IsLoading);
            window.Width = 800; window.Height = 600; shell.Text.SetLanguage("en-US"); Until(() => !current.IsLoading); Capture("results-narrow-en.png");
            shell.NavigateCommand.Execute("home"); Require(!shell.IsGlobalSearchOpen && overlay.Model is null, "navigation releases overlay");
            shell.OpenSearchCommand.Execute(null); Pump();
            var outside = new Point(10, 500); window.MouseDown(outside, MouseButton.Left); window.MouseUp(outside, MouseButton.Left); Pump();
            Require(!shell.IsGlobalSearchOpen, "outside click closes overlay");
            Console.WriteLine($"Global search passed in {clock.Elapsed.TotalSeconds:F2}s: real isolated SQLite, native F4/repeat/focus/arrows/Esc/click, bounded results, cancellation, detail/settings/page navigation, fixture playback, light/dark/narrow rendering. Audio is a protocol fixture.");
        }
        finally
        {
            window.ReleaseLibraryPage(); window.Close(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization();
            Wait(session.DisposeAsync().AsTask()); Wait(artwork.DisposeAsync().AsTask()); Wait(database.DisposeAsync().AsTask());
        }
        void Seed()
        {
            var root = Path.Combine(output, "media"); Directory.CreateDirectory(root);
            File.Copy("src/Echo.Desktop/Resources/Artwork/placeholder-light.png", Path.Combine(root, "cover.png"), true);
            var begin = database.BeginScanAsync(root, false, CancellationToken.None); Wait(begin);
            var entries = Enumerable.Range(1, 40).Select(index =>
            {
                var path = Path.Combine(root, $"{index:D2}.flac"); File.WriteAllBytes(path, "fLaC"u8.ToArray());
                return new ScanEntry(new(path, LibraryPath.Key(path), 4, 123), ScanEntryKind.Metadata,
                    new($"Aurora {index:D2}", "Aurora", "Aurora", "Aurora", 180 + index, "FLAC", 48000, 24, index));
            }).ToArray();
            Wait(database.CommitScanBatchAsync(begin.Result, 1, entries, CancellationToken.None));
            Wait(database.FinishScanAsync(begin.Result, ScanStatus.Completed, entries.Length, false, null, CancellationToken.None));
            var playlist = database.CreatePlaylistAsync(Guid.NewGuid().ToString("N"), "Night Drive", "Evening playlist"); Wait(playlist);
            var tracks = database.GetTracksAsync(new(1, "Aurora 01")); Wait(tracks);
            Wait(database.AddPlaylistTracksAsync(Guid.NewGuid().ToString("N"), playlist.Result, [tracks.Result.Items[0].Id]));
        }
        void Press(Key key)
        {
            var physical = key switch { Key.Down => PhysicalKey.ArrowDown, Key.Up => PhysicalKey.ArrowUp,
                Key.F4 => PhysicalKey.F4, Key.Enter => PhysicalKey.Enter, _ => PhysicalKey.Escape };
            window.KeyPress(key, RawInputModifiers.None, physical, null); window.KeyRelease(key, RawInputModifiers.None, physical, null); Pump();
        }
        void Capture(string name)
        {
            Pump(); window.UpdateLayout(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
            bitmap.Render(window); bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
        void Pump() => Dispatcher.UIThread.RunJobs();
        void Until(Func<bool> condition)
        { while (!condition()) { if (clock.Elapsed.TotalSeconds > 25) throw new TimeoutException("global search smoke"); Pump(); Thread.Sleep(3); } Pump(); }
        void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    }
    private static void Require(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); }
}
