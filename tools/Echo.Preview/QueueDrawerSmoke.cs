using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Queue;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Settings.Drawers;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Playback;

internal static class QueueDrawerSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var watch = Stopwatch.StartNew();
        var native = new QueueUiSmoke.PreviewNative();
        var session = new PlaybackSessionCoordinator(native);
        var shell = new ShellViewModel(); shell.Text.SetLanguage("zh-CN");
        var player = new PlayerViewModel(new PlaybackZone("local", "本机", session), shell.Text);
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork());
        shell.AttachPlayback(player); player.Queue.Artwork = artwork;
        shell.AttachLyrics(new EmptyLyricsSource(), new LyricsAppearance(text: shell.Text));
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        string[] titles = ["老人与海", "さよーならまたいつか！ - Sayonara", "青春病", "(2004)PRINCESS BRAVE!(130cm)",
            "01. Glimpse of Us", "02. 'Telephone' (Piano Arrange)", "Society", "No title", "14. 'Telephone' (Live at Ex Theater Roppongi 2019)", "&Z", "AIKA Sakura Trip"];
        string[] artists = ["ヨルシカ", "米津玄師", "藤井 風", "KOTOKO", "Unknown Artist", "Unknown Artist", "Eddie Vedder", "ギガP feat. れをる", "Unknown Artist", "SawanoHiroyuki[nZk]:mizuki", "Unknown Artist"];
        var tracks = titles.Select((title, i) => new QueueTrack($"drawer-{i}", $"track-{i}", $"preview-{i}.flac",
            new("http", "http://127.0.0.1/preview-only"),
            new Dictionary<string, string?> { ["title"] = title, ["artist"] = artists[i], ["duration"] = (201 + i * 11).ToString(),
                ["coverPath"] = Path.GetFullPath($"misc/queue-rework/media/album-{i % 4}/cover.png") },
            Origin: new("album", "歌曲列表", "preview-list"))).ToArray();
        try
        {
            window.Show(); Pump(session.SetQueueAsync(tracks, tracks[0].ItemId));
            native.Emit("audio.started", $"{{\"operationId\":{session.Snapshot.OperationId}}}");
            Pump(session.SetVolumeAsync(0));
            shell.NavigateCommand.Execute("queue"); Settle();
            var openButton = window.GetVisualDescendants().OfType<Button>().First(x => ReferenceEquals(x.Command, shell.ToggleQueueDrawerCommand));
            openButton.Focus(); shell.ToggleQueueDrawerCommand.Execute(null); Settle();
            var drawer = Drawer();
            var list = drawer.FindControl<QueueList>("PendingList")!;
            var overlay = window.GetVisualDescendants().OfType<QueueOverlay>().Single();
            Check(KeyboardNavigation.GetTabNavigation(overlay.FindControl<MotionSurface>("DrawerSurface")!) == KeyboardNavigationMode.Cycle, "Motion overwrote modal focus cycle");
            for (var i = 0; i < 24; i++)
            {
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); Settle();
                Check(overlay.IsKeyboardFocusWithin, "Tab escaped queue drawer");
            }
            Check(player.Queue.DrawerRows.Count == 10 && player.Queue.DrawerRows.All(x => !x.IsCurrent), "Current track duplicated in upcoming list");
            Check(player.Queue.PendingCountText == "10 首待播", "Upcoming count includes current track");
            Check(list.GetVisualDescendants().OfType<ListBoxItem>().Any(), "Drawer list did not bind");
            var before = player.Queue.DrawerRows;
            for (var i = 0; i < 30; i++) native.Emit("audio.position", $"{{\"operationId\":{session.Snapshot.OperationId},\"framesPlayed\":{i}}}");
            Pump(session.SetVolumeAsync(0)); Check(ReferenceEquals(before, player.Queue.DrawerRows), "Clock rebuilt upcoming rows");
            window.FocusManager?.Focus(null);
            foreach (var dark in new[] { false, true }) { shell.IsDark = dark; Save(dark ? "queue-window-dark.png" : "queue-window-light.png"); }
            var target = player.Queue.DrawerRows[0];
            var remove = list.GetVisualDescendants().OfType<Button>().First(x => x.Classes.Contains("drawer-remove") && x.DataContext == target);
            var point = remove.TranslatePoint(new Point(remove.Bounds.Width / 2, remove.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            WaitUntil(() => session.Queue.All(x => x.ItemId != target.ItemId));
            Pump(player.Queue.UndoCommand.ExecuteAsync(null)); Check(session.Queue.Any(x => x.ItemId == target.ItemId), "Single-row undo failed");
            drawer.FindControl<TextBox>("QueueSearch")!.Text = "No title"; Settle();
            Check(player.Queue.DrawerRows.Count == 1 && player.Queue.PendingCountText == "10 首待播", "Search changed total upcoming count");
            player.Queue.Search = ""; Settle();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle();
            Check(!shell.IsQueueDrawerOpen && !window.GetVisualDescendants().OfType<QueueDrawer>().Any(), "Closed drawer retained visuals");
            Check(window.FocusManager?.GetFocusedElement() == openButton, "Drawer failed to restore focus");
            shell.ToggleQueueDrawerCommand.Execute(null); Settle();
            window.MouseDown(new Point(500, 450), MouseButton.Left); window.MouseUp(new Point(500, 450), MouseButton.Left); Settle();
            Check(!shell.IsQueueDrawerOpen, "Outside click failed to close");
            shell.ToggleQueueDrawerCommand.Execute(null); Pump(session.SelectQueueItemAsync(tracks[3].ItemId));
            Check(player.Queue.DrawerRows.Count == 7, "Previously played album tracks entered upcoming list");
            var operation = session.Snapshot.OperationId; Pump(player.Queue.ClearCommand.ExecuteAsync(null));
            Check(player.Queue.IsDrawerEmpty && player.HasTrack && operation == session.Snapshot.OperationId, "Clear stopped current audio");
            Save("queue-window-empty.png"); Pump(player.Queue.UndoCommand.ExecuteAsync(null));
            var scaleTrack = tracks[0] with { Metadata = new Dictionary<string, string?> { ["title"] = "Scale track" } };
            Pump(session.SetQueueAsync(Enumerable.Range(0, 1000).Select(i => scaleTrack with { ItemId = $"large-{i}" }), "large-0"));
            Settle(); var realized = Drawer().GetVisualDescendants().OfType<ListBoxItem>().Count();
            Check(player.Queue.DrawerRows.Count == 999 && realized is > 0 and < 35, $"Drawer virtualization failed: {realized}");
            Pump(session.SetQueueAsync(tracks, tracks[0].ItemId));
            shell.Text.SetLanguage("en-US"); window.Width = 800; window.Height = 640; Save("queue-window-compact-en.png");
            Check(Drawer().Bounds.Right <= window.Bounds.Right && Drawer().Bounds.Height > 300, "Compact drawer escaped viewport");
            shell.UiScalePercent = 150; Settle();
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null); Settle();
            Check(Drawer().FindControl<TextBox>("QueueSearch")!.IsFocused, "Compact drawer search is inaccessible");
            Check(Drawer().FindControl<QueueList>("PendingList")!.Bounds.Height > 40, "Compact header consumed queue viewport");
            Save("queue-window-scaled.png"); shell.UiScalePercent = 100;
            shell.CloseQueueDrawerCommand.Execute(null); shell.NavigateCommand.Execute("lyrics"); shell.ToggleQueueDrawerCommand.Execute(null); Settle();
            Check(Drawer().IsEffectivelyVisible, "Queue unavailable above lyrics stage"); Save("queue-window-lyrics.png");
            Check(window.GetVisualDescendants().OfType<LyricsView>().Any(), "Lyrics stage was not mounted");
            window.Hide(); Settle();
            Check(!shell.IsQueueDrawerOpen && !window.GetVisualDescendants().OfType<QueueDrawer>().Any(), "Hidden window retained queue drawer");
            window.Show(); Settle();
            shell.ToggleQueueDrawerCommand.Execute(null); Settle();
            shell.OpenQuickSettingsCommand.Execute("lyrics"); Settle();
            var settings = window.GetVisualDescendants().OfType<QuickSettingsDrawer>().Single();
            Check(!shell.IsQueueDrawerOpen && settings.IsKeyboardFocusWithin, "Retiring queue stole settings focus");
            var search = settings.FindControl<TextBox>("DrawerSearchInput")!;
            search.Text = "desktop lyrics"; Settle();
            Check(settings.GetVisualDescendants().OfType<LyricsSettingsDrawerView>().Single().FindControl<TabControl>("LyricsTabs")!.SelectedIndex == 3, "Desktop lyrics missing from drawer search");
            shell.OpenSearchCommand.Execute(null); Settle();
            shell.OpenQuickSettingsCommand.Execute("lyrics"); Settle();
            Check(!shell.IsGlobalSearchOpen && shell.IsQuickSettingsOpen, "Search and quick settings overlap");
            Console.WriteLine($"PASS actual Avalonia drawer: row remove/undo, current/pending split, search, clear retains audio, 30 clocks retain rows, focus/Esc/outside close, English compact/lyrics, 999 rows/{realized} realized; simulated audio ({watch.ElapsedMilliseconds} ms).");
        }
        finally
        {
            player.Detach(); window.Close(); shell.Settings.Dispose();
            Pump(session.DisposeAsync().AsTask()); Pump(artwork.DisposeAsync().AsTask());
        }
        QueueDrawer Drawer() => window.GetVisualDescendants().OfType<QueueDrawer>().Single();
        void WaitUntil(Func<bool> condition)
        { while (!condition()) { Deadline(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } Settle(); }
        void Pump(Task task)
        {
            while (!task.IsCompleted) { Deadline(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            task.GetAwaiter().GetResult();
            WaitUntil(() => player.Queue.Version == session.QueueState.Version);
        }
        void Deadline() { if (watch.Elapsed.TotalSeconds > 20) throw new TimeoutException("Queue drawer short check"); }
        void Settle()
        { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); } AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Save(string name)
        {
            Settle(); using var frame = window.CaptureRenderedFrame() ?? throw new IOException("Queue drawer frame missing");
            frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); Console.WriteLine(Path.Combine(output, name));
        }
    }
    private sealed class EmptyLyricsSource : Echo.Contracts.Lyrics.ILyricsSource
    {
        public Task<Echo.Contracts.Lyrics.LyricsText?> ReadAsync(string audioPath, string? explicitPath, CancellationToken cancellation)
            => Task.FromResult<Echo.Contracts.Lyrics.LyricsText?>(null);
    }
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
