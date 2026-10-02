using System.Diagnostics;
using System.Text.Json;
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
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Playback;

internal static class QueueUiSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var clock = Stopwatch.StartNew();
        var native = new PreviewNative();
        var session = new PlaybackSessionCoordinator(native);
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session));
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork());
        var shell = new ShellViewModel(); shell.AttachPlayback(player); player.Queue.Artwork = artwork;
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        var fixture = Fixtures(output);
        QueueViewModel? queue = null;
        try
        {
            window.Show();
            Pump(session.SetQueueAsync(fixture, fixture[0].ItemId));
            native.Emit("audio.started", $"{{\"operationId\":{session.Snapshot.OperationId}}}");
            Pump(session.SetVolumeAsync(0));
            Pump(session.SeekAsync(69));
            Pump(session.PauseAsync());
            Pump(session.EnqueueAsync([fixture[4] with { ItemId = "next-one" }, fixture[5] with { ItemId = "next-two" }], true));
            shell.NavigateCommand.Execute("queue"); Settle();
            var list = window.GetVisualDescendants().OfType<QueueList>().First(x => !x.Compact);
            queue = (QueueViewModel)list.DataContext!;
            Check(queue!.Rows.Count == fixture.Length + 2, "queue page did not include album rows");
            list.LocateCurrent();
            var rows = queue!.Rows;
            for (var i = 0; i < 30; ++i) native.Emit("audio.position", $"{{\"operationId\":{session.Snapshot.OperationId},\"framesPlayed\":{i}}}");
            Pump(session.SetVolumeAsync(0));
            Check(ReferenceEquals(rows, queue!.Rows), "UI rows changed on position-only updates");
            queue!.Search = "魔术先生"; Settle(); Check(queue!.Rows.Count == 2, "queue search did not preserve occurrences");
            queue!.Search = "";
            Settle();
            var dragRows = list.GetVisualDescendants().OfType<ListBoxItem>().Where(x => x.DataContext is QueueRowViewModel).ToArray();
            var fromRow = dragRows.First(x => ((QueueRowViewModel)x.DataContext!).ItemId == "next-one");
            var toRow = dragRows.First(x => ((QueueRowViewModel)x.DataContext!).ItemId == "next-two");
            var fromPoint = fromRow.TranslatePoint(new Point(100, 25), window)!.Value;
            var toPoint = toRow.TranslatePoint(new Point(100, toRow.Bounds.Height - 6), window)!.Value;
            window.MouseMove(fromPoint); window.MouseDown(fromPoint, MouseButton.Left);
            window.MouseMove(toPoint); window.MouseUp(toPoint, MouseButton.Left);
            Pump(Task.Run(async () => { while (session.Queue[1].ItemId != "next-two") await Task.Delay(2); }));
            Check(session.Queue[2].ItemId == "next-one", "actual pointer reorder did not move after target");
            Pump(queue!.UndoCommand.ExecuteAsync(null));
            foreach (var dark in new[] { false, true })
            {
                shell.IsDark = dark; Settle();
                Capture(dark ? "queue-dark.png" : "queue-light.png");
            }
            window.KeyPress(Key.Q, RawInputModifiers.Control, PhysicalKey.Q, null); Settle(); Capture("queue-drawer-dark.png");
            Check(shell.IsQueueDrawerOpen, "drawer command failed");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle(); Check(!shell.IsQueueDrawerOpen, "drawer close failed");
            Pump(queue!.ShuffleCommand.ExecuteAsync(null)); Check(queue!.IsShuffle, "bound shuffle mode failed");
            Pump(queue!.SequentialCommand.ExecuteAsync(null));
            queue!.SetSelection([queue!.Rows[1]]);
            Pump(queue!.RemoveSelectedCommand.ExecuteAsync(null));
            Check(queue!.Rows.Count == fixture.Length + 1, "bound batch removal failed");
            Pump(queue!.UndoCommand.ExecuteAsync(null)); Check(queue!.Rows.Count == fixture.Length + 2, "bound undo failed");
            var operation = session.Snapshot.OperationId;
            Pump(queue!.ClearCommand.ExecuteAsync(null)); Settle();
            Check(queue!.IsEmpty && session.Snapshot.OperationId == operation, "clear/empty UI stopped current output");
            Capture("queue-empty-dark.png"); Pump(queue!.UndoCommand.ExecuteAsync(null));
            Pump(session.NextAsync()); Pump(session.NextAsync());
            shell.NavigateCommand.Execute("history"); Settle(); Capture("queue-history-dark.png");
            queue = ActiveQueue();
            Check(queue.ShowHistory && queue.Rows.Count > 0, "history route missing");
            queue.Search = "稻香";
            shell.ToggleQueueDrawerCommand.Execute(null); Settle();
            Check(queue.ShowHistory && queue.Search == "稻香" && !player.Queue.ShowHistory, "drawer changed history page projection");
            shell.CloseQueueDrawerCommand.Execute(null); queue.Search = "";
            shell.NavigateCommand.Execute("queue");
            queue = ActiveQueue();
            var many = Enumerable.Range(0, 1000).Select(i => fixture[i % fixture.Length] with { ItemId = $"scale-{i}" }).ToArray();
            Pump(session.SetQueueAsync(many, many[0].ItemId)); Settle();
            var realized = window.GetVisualDescendants().OfType<ListBoxItem>().Count();
            Check(realized < 60 && queue!.Rows.Count == 1000, $"queue not virtualized: {realized}");
            Capture("queue-1000-dark.png");
            Pump(session.SetQueueAsync(fixture, fixture[0].ItemId)); Pump(session.PauseAsync());
            window.Width = 900; window.Height = 720; Settle(); Capture("queue-compact-dark.png");
            Console.WriteLine($"PASS native Avalonia queue/drawer/history/empty/compact renders, bound edit/undo/modes/search, stable rows after 30 position events, 1000 rows / {realized} realized ({clock.ElapsedMilliseconds} ms)");
        }
        finally
        {
            player.Detach(); window.Close(); shell.Settings.Dispose();
            Pump(session.DisposeAsync().AsTask()); Pump(artwork.DisposeAsync().AsTask());
        }
        void Capture(string name)
        {
            Settle();
            using var bitmap = window.CaptureRenderedFrame() ?? throw new IOException("Native queue render unavailable");
            bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
            Console.WriteLine(Path.Combine(output, name));
        }
        void Pump(Task task)
        {
            while (!task.IsCompleted) { if (clock.Elapsed > TimeSpan.FromSeconds(18)) throw new TimeoutException("Queue UI short smoke"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); }
            task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
            while (queue is not null && queue.Version != session.QueueState.Version)
            { if (clock.Elapsed > TimeSpan.FromSeconds(18)) throw new TimeoutException("Queue projection update"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        }
        QueueViewModel ActiveQueue() => (QueueViewModel)window.GetVisualDescendants().OfType<QueueList>().First(x => !x.Compact).DataContext!;
    }
    private static void Settle()
    { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static QueueTrack[] Fixtures(string output)
    {
        string[] titles = ["稻香", "给我一首歌的时间", "蛇舞", "花海", "魔术先生", "说好的幸福呢", "兰亭序", "流浪诗人", "时光机", "乔克叔叔", "晴天", "七里香", "夜曲", "一路向北"];
        var origin = new QueueOrigin("album", "魔杰座", "preview-album");
        var media = Path.GetFullPath(Path.Combine(output, "..", "media"));
        return titles.Select((title, index) =>
        {
            var dir = Path.Combine(media, $"album-{index % 4}"); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"track-{index}.wav");
            if (!File.Exists(path)) File.Copy(Path.GetFullPath("misc/playback-smoke/a.wav"), path);
            var sourceCover = Path.GetFullPath($"misc/album-wall-validation/probe1/media/album-{index % 4:D5}/cover.png");
            if (File.Exists(sourceCover) && !File.Exists(Path.Combine(dir, "cover.png"))) File.Copy(sourceCover, Path.Combine(dir, "cover.png"));
            return new QueueTrack($"preview-{index}", $"track-{index}", path, Metadata:
                new Dictionary<string, string?> { ["title"] = title, ["artist"] = "周杰伦", ["album"] = index < 10 ? "魔杰座" : "精选", ["codec"] = "flac", ["duration"] = (189 + index * 7).ToString() }, Origin: origin);
        }).ToArray();
    }
    internal sealed class PreviewNative : INativeAudio
    {
        private int operation;
        public event Action<string, JsonElement>? Notification;
        public event Action<Exception>? Failed { add { } remove { } }
        public void Emit(string method, string json) { using var doc = JsonDocument.Parse(json); Notification?.Invoke(method, doc.RootElement.Clone()); }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            var json = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openFile" or "audio.openSource" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":223}}",
                _ => "true"
            };
            using var doc = JsonDocument.Parse(json); return Task.FromResult(doc.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
