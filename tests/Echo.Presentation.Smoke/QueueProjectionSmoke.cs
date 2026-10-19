using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Queue;
using Echo.Desktop.Shell;
using Echo.Playback;

internal static class QueueProjectionSmoke
{
    public static void Run()
    {
        var native = new NativeFixture();
        var session = new PlaybackSessionCoordinator(native);
        var shell = new ShellViewModel();
        var player = new PlayerViewModel(new PlaybackZone("fixture", "fixture", session), shell.Text);
        shell.AttachPlayback(player);
        var tracks = Enumerable.Range(0, 6).Select(i => new QueueTrack("item-" + i, "same-track", "fixture.flac",
            new("http", "https://fixture/audio"), new Dictionary<string, string?> { ["title"] = "Song " + i, ["artist"] = "Artist" },
            Origin: new("album", "Fixture", "album"))).ToArray();
        var drawer = new QueueDrawer { DataContext = shell };
        var window = new Window { Content = drawer, Width = 440, Height = 640 };
        try
        {
            Change(session.SetQueueAsync(tracks, tracks[0].ItemId));
            Check(player.Queue.Rows.Count == 0, "closed drawer creates no row projection");
            window.Show(); Wait(() => player.Queue.Rows.Count == 6);
            var original = player.Queue.Rows.ToArray();
            Check(original.Select(row => row.ItemId).Distinct().Count() == 6, "separate queue occurrences of one track remain separate");
            var resets = 0;
            ((INotifyCollectionChanged)player.Queue.Rows).CollectionChanged += (_, change) =>
            { if (change.Action == NotifyCollectionChangedAction.Reset) resets++; };
            Change(session.SelectQueueItemAsync(tracks[1].ItemId));
            Check(original.Zip(player.Queue.Rows).All(pair => ReferenceEquals(pair.First, pair.Second))
                && !original[0].IsCurrent && original[1].IsCurrent, "switch updates flags without rebuilding rows");
            Change(session.SelectQueueItemAsync(tracks[0].ItemId));
            var list = drawer.FindControl<QueueList>("PendingList")!.FindControl<ListBox>("Tracks")!;
            list.SelectedItem = original[2];
            player.Queue.Search = "Song 2";
            Check(player.Queue.Rows.Count == 1 && ReferenceEquals(original[2], player.Queue.Rows[0]), "filter reuses matching row");
            player.Queue.Search = "";
            Change(session.MoveQueueItemsAsync([tracks[2].ItemId], 1));
            Check(ReferenceEquals(player.Queue.Rows[1], original[2]) && original[2].Index == 1
                && list.SelectedItems!.Contains(original[2]), "reorder preserves row identity and actual list selection");
            player.Queue.SetSelection([original[2]]);
            Change(player.Queue.RemoveSelectedCommand.ExecuteAsync(null));
            Check(player.Queue.Rows.All(row => row.ItemId != tracks[2].ItemId), "selected-row removal still reaches the session");
            Change(player.Queue.UndoCommand.ExecuteAsync(null));
            Check(player.Queue.Rows.Count == 6 && resets == 0, "edits and undo reconcile without a full reset");
            Change(session.SelectQueueItemAsync(tracks[1].ItemId));
            Change(session.SelectQueueItemAsync(tracks[0].ItemId));
            using var history = new QueueViewModel(session, shell.Text) { ShowHistory = true };
            Check(history.Rows.Count == session.QueueState.History.Count
                && history.Rows.Distinct().Count() == history.Rows.Count
                && history.Rows.Select(row => row.ItemId).Distinct().Count() < history.Rows.Count, "duplicate history plays have independent rows");
            window.Hide(); Wait(() => player.Queue.Rows.Count == 0);
            Check(session.Queue.Count == 6, "hiding releases presentation without changing the queue");
            window.Show(); Wait(() => player.Queue.Rows.Count == 6);
            Check(player.Queue.Rows.Single(row => row.IsCurrent).ItemId == session.QueueState.CurrentItemId, "reopen restores authoritative current item");
            Console.WriteLine("PASS queue: demand lifecycle, row reuse, duplicate history, selection/reorder/remove/undo and no collection reset");
        }
        finally
        {
            window.Close(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization(); Pump(session.DisposeAsync().AsTask());
        }
        void Change(Task task) { Pump(task); Wait(() => player.Queue.Version == session.QueueState.Version); }
    }
    internal static void Pump(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    internal static void Wait(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Queue/search smoke timed out.");
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
    }
    internal static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class NativeFixture : INativeAudio
    {
        private int operation;
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken token = default)
        {
            var json = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openFile" or "audio.openSource" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":200}}",
                _ => "true"
            };
            using var document = JsonDocument.Parse(json); return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
