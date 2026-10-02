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
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.History;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal static class HistorySmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var watch = Stopwatch.StartNew(); var checks = 0;
        var path = Path.Combine(output, "history-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var media = Path.Combine(output, "media"); Directory.CreateDirectory(media);
        var opening = LibraryDatabase.OpenAsync(path, budget.Token); Pump(opening); var db = opening.Result;
        var scan = db.BeginScanAsync(media, false, budget.Token); Pump(scan);
        var entries = Enumerable.Range(0, 8).Select(i =>
        {
            var sample = HistoryPreviewFixture.Create(media,i); var file = sample.Path;
            return new ScanEntry(new(file, LibraryPath.Key(file), 44, 1), ScanEntryKind.Metadata,
                new(sample.Title,sample.Artist,sample.Album,sample.Artist,180,"FLAC",96000,24));
        }).ToArray();
        Pump(db.CommitScanBatchAsync(scan.Result, 1, entries, budget.Token));
        Pump(db.FinishScanAsync(scan.Result, ScanStatus.Completed, 8, false, null, budget.Token));
        Pump(db.DisposeAsync().AsTask());
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Offline fixture generation has the only writer lock; production APIs own every subsequent mutation.
        using (var owner = new FileStream(path + ".writer.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open(); connection.CreateFunction<string, string>("echo_fold", s => s.Normalize().ToLowerInvariant());
            using var command = connection.CreateCommand(); command.Parameters.AddWithValue("$now", now);
            command.CommandText = """
                WITH RECURSIVE numbers(n) AS(SELECT 0 UNION ALL SELECT n+1 FROM numbers WHERE n<49999),
                seeds AS(SELECT *,row_number() OVER(ORDER BY id)-1 ordinal FROM tracks)
                INSERT INTO listening_events(id,track_id,started,updated,seconds,ended,title,artist,album,path,duration,codec,source,history_epoch,counted,sample_rate,bit_depth)
                SELECT printf('seed-%06d',n),t.id,$now-(n/50)*3600-(n%50)*45,$now-(n/50)*3600-(n%50)*45+120,
                  CASE WHEN n%5=0 THEN 12 ELSE 120 END,$now-(n/50)*3600-(n%50)*45+120,
                  t.title,t.artist,t.album,t.path,t.duration,t.codec,'曲目',0,n%5<>0,t.sample_rate,t.bit_depth
                FROM numbers JOIN seeds t ON t.ordinal=n%8;
                """;
            command.ExecuteNonQuery();
        }
        opening = LibraryDatabase.OpenAsync(path, budget.Token); Pump(opening); db = opening.Result;
        var native = new QueueUiSmoke.PreviewNative();
        var runtime = new Echo.Application.Playback.PlaybackRuntime(native);
        MainWindow? window = null;
        try
        {
            var queryWatch = Stopwatch.StartNew();
            var first = db.GetHistoryAsync(new(), cancellation: budget.Token); Pump(first); var firstMs = queryWatch.Elapsed.TotalMilliseconds;
            Require(first.Result.Total == 50000 && first.Result.Items.Count == 200 && first.Result.Next is not null, "bounded first page and count");
            var second = db.GetHistoryAsync(new(), first.Result.Next, cancellation: budget.Token); Pump(second);
            Require(!first.Result.Items.Select(x => x.Id).Intersect(second.Result.Items.Select(x => x.Id)).Any(), "stable cursor pages");
            var back = db.GetHistoryAsync(new(), second.Result.Previous, cancellation: budget.Token); Pump(back);
            Require(back.Result.Items.Select(x => x.Id).SequenceEqual(first.Result.Items.Select(x => x.Id)), "bounded backward cursor");
            var search = db.GetHistoryAsync(new("夜航"), cancellation: budget.Token); Pump(search);
            Require(search.Result.Total == 25000 && search.Result.Items.All(x => x.Track.Title.Contains("夜航")), "Chinese snapshot search");
            var counted = db.GetHistoryAsync(new(CountedOnly: true), cancellation: budget.Token); Pump(counted);
            Require(counted.Result.Total == 40000, "old counted qualification");
            var track = first.Result.Items[0].Track;
            var epoch = db.GetHistoryEpochAsync(budget.Token); Pump(epoch);
            var live = new ListeningEntry("zz-live-event", track.Id, track.Path, now, now, 0, Source: "历史", HistoryEpoch: epoch.Result);
            Pump(db.RecordListeningAsync(live, budget.Token));
            var afterInsert = db.GetHistoryAsync(new(), first.Result.Next, cancellation: budget.Token); Pump(afterInsert);
            Require(afterInsert.Result.Items.All(x => x.Id != live.Id), "late inserts excluded from page snapshot");
            Pump(db.RecordListeningAsync(live with { PlayedSeconds = 30, UpdatedUnix = now+32, EndedUnix = now+32 }, budget.Token));
            var completed = db.GetHistoryAsync(new(), cancellation: budget.Token); Pump(completed);
            Require(completed.Result.Items[0] is { Counted: true, EndedUnix: not null, Source: "历史", Track.SampleRate: 96000 }, "completion, source and quality snapshots");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); var task = db.GetHistoryAsync(new(), cancellation: cancelled.Token);
                try { Pump(task); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { checks++; }
            }
            queryWatch.Restart(); var review = db.GetHistoryReviewAsync(new(), budget.Token); Pump(review);
            var reviewMs = queryWatch.Elapsed.TotalMilliseconds;
            Require(review.Result.Totals.Plays == 50001 && review.Result.Days.Count <= 366 && review.Result.Tracks.Count <= 8 && review.Result.Quality.Count > 0, "bounded review aggregates");
            window = DesktopComposition.CreateWindow(false, false, db, playback: runtime);
            var shell = (ShellViewModel)window.DataContext!;
            shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
            window.Width = 1440; window.Height = 900; window.Show(); shell.NavigateCommand.Execute("history"); Settle();
            var view = window.GetVisualDescendants().OfType<HistoryView>().Single(); var model = view.Model!;
            Pump(model.LoadAsync(reset: true)); Settle();
            var list = view.FindControl<ListBox>("RecordsList")!;
            Require(list.GetRealizedContainers().Count() < 40 && model.Items.Count == 200, "native virtualized records");
            model.Selected = model.Items[0]; Pump(model.AddCommand.ExecuteAsync(null));
            Require(shell.Player.Session.Queue.Count > 0, "history to actual queue command");
            var detailButton = view.GetVisualDescendants().OfType<Button>().First(button => button.Tag as string == "history-details");
            var detailPoint = detailButton.TranslatePoint(new Point(detailButton.Bounds.Width/2,detailButton.Bounds.Height/2),window)!.Value;
            var operation = shell.Player.Session.Snapshot.OperationId;
            window.MouseDown(detailPoint,MouseButton.Left); window.MouseUp(detailPoint,MouseButton.Left);
            window.MouseDown(detailPoint,MouseButton.Left); window.MouseUp(detailPoint,MouseButton.Left); Settle();
            Require(shell.Player.Session.Snapshot.OperationId == operation,"native double-click on details does not start playback");
            model.Items[0].Expanded = false; Settle(); Save("history-light.png");
            model.Items[0].Expanded = true; Settle(); Save("history-details-light.png"); model.Items[0].Expanded = false;
            shell.IsDark = true; Settle(); Save("history-dark.png");
            var reviewButton=view.GetVisualDescendants().OfType<Button>().Single(button => button.CommandParameter as string == "review");
            var reviewPoint=reviewButton.TranslatePoint(new Point(reviewButton.Bounds.Width/2,reviewButton.Bounds.Height/2),window)!.Value;
            window.MouseDown(reviewPoint,MouseButton.Left); window.MouseUp(reviewPoint,MouseButton.Left);
            Require(model.IsReview,"native review tab input");
            Pump(model.LoadAsync(reset: true)); Settle(); Save("history-review-dark.png");
            shell.IsDark = false; Settle(); Save("history-review-light.png");
            model.ShowDay(DateTime.Now.ToString("yyyy-MM-dd")); Pump(model.LoadAsync(reset: true));
            Require(model.IsCustom && model.IsRecords && model.Items.Count > 0, "activity day drilldown");
            window.Width = 960; window.Height = 760; Settle(); Save("history-compact.png");
            model.IsReview = true; Pump(model.LoadAsync(reset: true)); Settle(); Save("history-review-compact.png");
            model.IsReview = false; Pump(model.LoadAsync(reset: true));
            model.FromDate = "bad-date"; Pump(model.ApplyDatesCommand.ExecuteAsync(null));
            Require(model.Error.Length > 0 && !model.CanPlay, "invalid range disables stale actions");
            model.RangeIndex = 0; Pump(model.LoadAsync(reset: true));
            // Exercise the real desktop recorder against the same persistent owner. Native decoding is simulated.
            model.Selected = model.Items[0]; Pump(model.PlayCommand.ExecuteAsync(null));
            native.Emit("audio.started", "{\"operationId\":" + shell.Player.Session.Snapshot.OperationId + "}");
            var recording = ReadUntil(data => data.Total >= 50002 && data.Items[0].EndedUnix is null);
            var recordingId = recording.Items[0].Id;
            Pump(shell.Player.Session.SeekAsync(100,budget.Token)); Pump(shell.Player.Session.PauseAsync(budget.Token));
            Pump(shell.Player.Session.StopAsync(budget.Token));
            var stopped = ReadUntil(data => data.Items.Any(row => row.Id == recordingId && row.EndedUnix is not null));
            Require(stopped.Total == 50002 && stopped.Items.Single(row => row.Id == recordingId).PlayedSeconds < 3,
                "desktop recorder preserves one occurrence across seek/pause and excludes seek distance");
            Pump(model.LoadAsync(reset: true));
            model.RequestClearCommand.Execute(null); model.CancelClearCommand.Execute(null);
            Require(!model.ConfirmClear && model.Items.Count > 0, "clear confirmation cancellation");
            model.RequestClearCommand.Execute(null); Pump(model.ClearCommand.ExecuteAsync(null));
            Require(model.IsEmpty, "confirmed clear and empty state");
            Pump(db.RecordListeningAsync(live with { PlayedSeconds = 60, UpdatedUnix = now+61, EndedUnix = now+61 }, budget.Token));
            var empty = db.GetHistoryAsync(new(), cancellation: budget.Token); Pump(empty);
            Require(empty.Result.Total == 0, "cleared active event cannot resurrect");
            var oldCursor = db.GetHistoryAsync(new(), first.Result.Next, cancellation: budget.Token);
            try { Pump(oldCursor); throw new Exception("Old epoch cursor accepted"); } catch (LibraryChangedException) { checks++; }
            shell.NavigateCommand.Execute("songs"); Settle();
            Require(model.Items.Count == 0, "navigation releases records and subscribers");
            File.WriteAllText(Path.Combine(output,"history-results.txt"), $"Checks: {checks}\nFixture: 50000 generated listening events / 8 local tracks; simulated native playback\nFirst page: {firstMs:F2} ms (1 sample)\nReview: {reviewMs:F2} ms (1 sample)\nElapsed: {watch.Elapsed.TotalSeconds:F2} s\n");
            Console.WriteLine(File.ReadAllText(Path.Combine(output,"history-results.txt")));
        }
        finally
        {
            if (window is not null) { window.RequestExit(); Settle(); }
            Pump(runtime.DisposeAsync().AsTask()); Pump(db.DisposeAsync().AsTask());
        }
        void Require(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks++; }
        HistoryPage ReadUntil(Func<HistoryPage,bool> ready)
        {
            var until=Stopwatch.StartNew();
            while(true)
            {
                var read=db.GetHistoryAsync(new(),cancellation:budget.Token); Pump(read);
                if(ready(read.Result)) return read.Result;
                if(until.Elapsed.TotalSeconds>2) throw new TimeoutException("Desktop listening recorder confirmation");
                Thread.Sleep(5); Dispatcher.UIThread.RunJobs();
            }
        }
        void Pump(Task task)
        {
            while (!task.IsCompleted) { budget.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        }
        void Settle()
        {
            for(var i=0;i<8;i++) { budget.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); }
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        }
        void Save(string name)
        {
            using var image = window!.CaptureRenderedFrame() ?? throw new IOException("History frame unavailable");
            image.Save(Path.Combine(output,name), PngBitmapEncoderOptions.Default); Console.WriteLine(Path.Combine(output,name));
        }
    }
}
