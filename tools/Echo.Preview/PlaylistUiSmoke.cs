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
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Playlists;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playback;
using SkiaSharp;

// Real isolated SQLite + real Avalonia rendering/input. Audio confirmations are a fixture.
internal static class PlaylistUiSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(32));
        var path = Path.Combine(output, "library-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var opening = LibraryDatabase.OpenAsync(path, budget.Token); Pump(opening); var database = opening.Result;
        var runtime = new Echo.Application.Playback.PlaybackRuntime(new PreviewNative());
        MainWindow? window = null;
        try
        {
            var root = Path.Combine(output, "music"); Directory.CreateDirectory(root);
            var entries = new List<ScanEntry>();
            string[] names = ["夜晚的回声", "Moonlight Radio", "城市慢慢入睡", "After the Rain", "蓝色时刻", "Somewhere Far Away", "回到海边", "Daybreak", "Last Signal"];
            for (var i = 0; i < 245; i++)
            {
                var folder = Path.Combine(root, "album-" + i % 5); Directory.CreateDirectory(folder);
                if (i < 5) Cover(folder, i);
                var file = Path.Combine(folder, $"{i:000}.flac"); File.WriteAllBytes(file, [0]);
                entries.Add(new(new(file, LibraryPath.Key(file), 24_000_000, 1), ScanEntryKind.Metadata,
                    new(i < names.Length ? names[i] : $"Live Session {i:000}", "ECHO Ensemble", "Midnight Letters · " + i % 5,
                        "ECHO Ensemble", 180 + i, "flac", 96000, 24, i + 1)));
            }
            var scan = database.BeginScanAsync(root, false, budget.Token); Pump(scan);
            var sequence = 0;
            foreach (var batch in entries.Chunk(200)) Pump(database.CommitScanBatchAsync(scan.Result, ++sequence, batch, budget.Token));
            Pump(database.FinishScanAsync(scan.Result, ScanStatus.Completed, 245, false, null, budget.Token));
            var read = database.GetTracksAsync(new(200), budget.Token); Pump(read);
            var rows = read.Result.Items;
            var op = Guid.NewGuid().ToString("N");
            var create = database.CreatePlaylistAsync(op, "01 · 夜行电台", "留给夜晚的音乐，慢慢听。", budget.Token); Pump(create); var id = create.Result;
            var repeat = database.CreatePlaylistAsync(op, "01 · 夜行电台", "留给夜晚的音乐，慢慢听。", budget.Token); Pump(repeat);
            Require(repeat.Result == id, "create receipt idempotency");
            var addOp = Guid.NewGuid().ToString("N");
            Pump(database.AddPlaylistTracksAsync(addOp, id, rows.Take(9).Select(row => row.Id).ToArray(), budget.Token));
            Pump(database.AddPlaylistTracksAsync(addOp, id, rows.Take(9).Select(row => row.Id).ToArray(), budget.Token));
            Pump(database.AddPlaylistTracksAsync(Op(), id, [rows[0].Id], budget.Token));
            var items = database.GetPlaylistTracksAsync(new(id), budget.Token); Pump(items);
            Require(items.Result.Total == 10 && items.Result.Items.Select(item => item.ItemId).Distinct().Count() == 10, "occurrences and add receipt");
            var first = items.Result.Items[0]; var second = items.Result.Items[1];
            Pump(database.MovePlaylistItemAsync(Op(), id, second.ItemId, true, items.Result.Revision, budget.Token));
            var moved = database.GetPlaylistTracksAsync(new(id), budget.Token); Pump(moved);
            Require(moved.Result.Items[0].ItemId == second.ItemId, "manual order swap");
            Pump(database.RemovePlaylistItemsAsync(Op(), id, [moved.Result.Items[^1].ItemId], budget.Token));
            var large = database.CreatePlaylistAsync(Op(), "05 · 现场收藏", "Live Sessions", budget.Token); Pump(large);
            Pump(database.AddPlaylistTracksAsync(Op(), large.Result, rows.Select(row => row.Id).ToArray(), budget.Token));
            var tail = database.GetTracksAsync(new(200, Offset: 200), budget.Token); Pump(tail);
            Pump(database.AddPlaylistTracksAsync(Op(), large.Result, tail.Result.Items.Select(row => row.Id).ToArray(), budget.Token));
            foreach (var (name, index) in new[] { "02 · 海岸线", "03 · 晴天散步", "04 · 安静工作", "06 · 周末午后" }.Select((name, index) => (name, index)))
            {
                var more = database.CreatePlaylistAsync(Op(), name, "ECHO music collection", budget.Token); Pump(more);
                Pump(database.AddPlaylistTracksAsync(Op(), more.Result, rows.Skip(index * 4).Take(4).Select(row => row.Id).ToArray(), budget.Token));
            }
            foreach (var row in rows.Take(7)) Pump(database.SetTrackFavoriteAsync(row.Id, true, budget.Token));
            var albumRead = database.GetAlbumsAsync(new(), budget.Token); Pump(albumRead);
            Pump(database.SetAlbumStateAsync(albumRead.Result.Items[0].Id, true, 4, budget.Token));
            var bounded = database.GetPlaylistTracksAsync(new(large.Result), budget.Token); Pump(bounded);
            Require(bounded.Result.Total == 245 && bounded.Result.Items.Count == 200, "bounded large playlist");
            var stale = database.MovePlaylistItemAsync(Op(), id, first.ItemId, false, items.Result.Revision, budget.Token);
            try { Pump(stale); throw new InvalidOperationException("stale reorder accepted"); } catch (LibraryChangedException) { }
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { Pump(database.GetPlaylistsAsync(new(), cancel.Token)); throw new InvalidOperationException("cancel ignored"); } catch (OperationCanceledException) { }

            window = DesktopComposition.CreateWindow(false, false, database, playback: runtime);
            var shell = (ShellViewModel)window.DataContext!; shell.Text.SetLanguage("zh-CN");
            shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
            window.Show(); Settle(); shell.NavigateCommand.Execute("playlists"); Settle();
            var view = window.GetVisualDescendants().OfType<PlaylistsView>().Single(); var model = view.Model;
            Wait(() => !model.IsBrowsing && model.Playlists.Count == 6);
            Wait(() => view.GetVisualDescendants().OfType<PlaylistTile>().Count() == 6);
            Save("playlists-home-light.png");
            Click(view.FindControl<Button>("NewButton")!);
            view.FindControl<TextBox>("NameField")!.Text = "测试歌单";
            view.FindControl<TextBox>("DescriptionField")!.Text = "由真实页面创建";
            Click(view.FindControl<Button>("SaveButton")!);
            Wait(() => !model.IsActing && model.IsManual && model.SelectedPlaylist?.Name == "测试歌单");
            Click(view.FindControl<Button>("AddButton")!);
            var picker = window.GetVisualDescendants().OfType<PlaylistTrackPicker>().Single();
            var pickList = picker.FindControl<ListBox>("TrackList")!;
            Wait(() => pickList.ItemCount == 200 && pickList.IsEnabled);
            pickList.SelectedItem = pickList.Items[0]; Settle();
            Click(picker.FindControl<Button>("AddButton")!);
            Wait(() => !model.IsActing && model.Tracks.Count == 1 && !view.FindControl<ContentControl>("PickerHost")!.IsVisible);
            Require(model.TotalTracks == 1, "native create/add workflow");
            Pump(model.ArchiveCommand.ExecuteAsync(null)); Require(model.IsHome && model.CanUndo, "archive and undo affordance");
            Pump(model.UndoCommand.ExecuteAsync(null)); Require(!model.CanUndo && model.Playlists.Any(row => row.Name == "测试歌单"), "undo restores persisted playlist");
            model.Cards = true; Settle(); Wait(() => view.GetVisualDescendants().OfType<PlaylistTile>().Count() > 0); Save("playlists-cards-light.png");
            shell.IsDark = true; Settle(); Save("playlists-cards-dark.png"); shell.IsDark = false; Settle();
            model.OpenPlaylist(model.Playlists.Single(row => row.Id == id)); Wait(() => !model.IsLoading && model.Tracks.Count == 9);
            Save("playlist-detail-light.png");
            CheckAlignment(view);
            Click(view.FindControl<Button>("PlayButton")!); Wait(() => !model.IsActing);
            Require(runtime.LocalZone.Session.QueueState.Items.Count == 9 && runtime.LocalZone.Session.QueueState.Items[0].Origin?.Kind == "playlist", "playlist playback queue and origin");
            shell.IsDark = true; Settle(); Save("playlist-detail-dark.png");
            model.Search = "nothing-matches"; model.Search = "Moonlight";
            Wait(() => !model.IsLoading && model.TotalTracks == 0); // The first nine alphabetical rows do not include this title.
            model.Search = ""; Wait(() => !model.IsLoading && model.TotalTracks == 9);
            model.OpenPlaylist(model.Playlists.Single(row => row.Id == large.Result)); Wait(() => !model.IsLoading && model.TotalTracks == 245);
            var realized = view.GetVisualDescendants().OfType<ListBoxItem>().Count(item => item.DataContext is PlaylistTrackItem);
            Require(realized < 35 && model.Tracks.Count == 200, "finite realized rows");
            Pump(model.NextCommand.ExecuteAsync(null)); Require(model.Tracks.Count == 45, "next page");
            Pump(model.PreviousCommand.ExecuteAsync(null)); Require(model.Tracks.Count == 200, "previous page");
            model.SortIndex = 4; Wait(() => !model.IsLoading && model.Tracks.Count > 0); Require(model.Tracks[0].Row.DurationSeconds == 180, "SQL duration order");
            model.Open("liked"); Wait(() => !model.IsLoading && model.TotalTracks == 7); Save("liked-tracks-dark.png");
            model.Open("albums"); Settle(); Wait(() => window.GetVisualDescendants().OfType<Echo.Desktop.Features.Albums.AlbumCard>().Any()); Save("liked-albums-dark.png");
            model.OpenPlaylist(model.Playlists.Single(row => row.Id == id)); Wait(() => !model.IsLoading && model.TotalTracks == 9);
            window.Width = 980; Settle(); CheckAlignment(view); Save("playlist-detail-narrow.png");
            shell.Text.SetLanguage("en-US"); Settle(); Save("playlist-detail-english.png");
            model.Search = "no-results"; Wait(() => !model.IsLoading && model.IsEmpty); Save("playlist-empty.png");
            shell.NavigateCommand.Execute("home"); Settle(); Require(model.Tracks.Count == 0 && model.Playlists.Count == 0, "navigation disposal");
            Console.WriteLine("PASS playlist: isolated SQLite receipts, duplicate occurrences, manual order/revision, cancellation, bounded 245-track pages, favorites, real create/add/undo UI input, queue origin, virtualization, themes/cards/narrow/language renders and disposal. Audio simulated.");

            void Click(Button button)
            { var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value; window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle(); }
            void Save(string name)
            { window.MouseMove(new Point(window.Bounds.Width - 20, 50)); Settle(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96)); bitmap.Render(window); bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); Console.WriteLine(name); }
        }
        finally
        {
            if (window is not null) { window.RequestExit(); Wait(() => !window.IsVisible); }
            Pump(runtime.DisposeAsync().AsTask()); Pump(database.DisposeAsync().AsTask());
        }
        void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Wait(Func<bool> ready) { while (!ready()) { budget.Token.ThrowIfCancellationRequested(); Settle(); Thread.Sleep(2); } Settle(); }
        void Pump(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    }
    private static string Op() => Guid.NewGuid().ToString("N");
    private static void CheckAlignment(PlaylistsView view)
    {
        var grids = view.GetVisualDescendants().OfType<Echo.Desktop.Features.Folders.FolderColumnsGrid>().ToArray();
        var header = grids.Single(grid => grid.Name == "HeaderColumns"); var row = grids.First(grid => grid.Name is null);
        for (var i = 0; i < 5; i++)
        {
            var a = header.Children.First(control => Grid.GetColumn(control) == i); var b = row.Children.First(control => Grid.GetColumn(control) == i);
            if (!a.IsVisible) continue;
            var ax = a.TranslatePoint(default, view)!.Value.X + (i == 4 ? a.Bounds.Width : 0);
            var bx = b.TranslatePoint(default, view)!.Value.X + (i == 4 ? b.Bounds.Width : 0);
            Require(Math.Abs(ax - bx) < 1, $"header/row column {i} alignment: {ax} vs {bx}");
        }
    }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static void Cover(string folder, int index)
    {
        string[] colors = ["#253f69", "#477c88", "#704251", "#304f43", "#715b3a"];
        using var bitmap = new SKBitmap(256, 256); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColor.Parse(colors[index]));
        using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#d1d7e2"), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
        for (var i = 0; i < 9; i++) canvas.DrawCircle(128, 128, 20 + i * 12, paint);
        paint.Style = SKPaintStyle.Fill; canvas.DrawCircle(128, 128, 18, paint);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(folder, "cover.png")); data.SaveTo(file);
    }
    private sealed class PreviewNative : INativeAudio
    {
        private long operation;
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            var json = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openFile" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":223}}", _ => "true"
            };
            using var document = JsonDocument.Parse(json); return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
