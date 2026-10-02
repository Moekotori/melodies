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
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Folders;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playback;
using SkiaSharp;

// Actual Avalonia controls and isolated new-format SQLite. Audio acknowledgements are simulated.
internal static class FolderUiSmoke
{
    public static void Run(string output, bool isolated = false)
    {
        Directory.CreateDirectory(output);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var root = Path.Combine(output, "音乐收藏");
        var opening = LibraryDatabase.OpenAsync(Path.Combine(output, "library-" + Guid.NewGuid().ToString("N") + ".sqlite"), budget.Token);
        Pump(opening); var database = opening.Result;
        var runtime = new Echo.Application.Playback.PlaybackRuntime(new PreviewNative());
        MainWindow? window = null;
        Action? previewLanguage = null;
        try
        {
            string[] albums = ["01 · 夜行电台", "02 · 海岸线", "03 · 现场录音"];
            var entries = new List<ScanEntry>();
            foreach (var (album, index) in albums.Select((name, index) => (name, index)))
            {
                var folder = Path.Combine(root, album); Directory.CreateDirectory(folder); Cover(folder, index);
                var names = index == 0 ? new[] { "夜晚的回声", "Moonlight Radio", "城市慢慢入睡", "After the Rain", "蓝色时刻", "Somewhere Far Away", "回到海边", "Daybreak", "Last Signal" }
                    : index == 1 ? new[] { "海风", "Coastline", "潮汐", "A Quiet Morning", "远方来信", "Sunset Drive", "路过夏天", "Home Again" }
                    : Enumerable.Range(1, 240).Select(i => $"现场片段 {i:000}").ToArray();
                for (var i = 0; i < names.Length; i++)
                {
                    var path = Path.Combine(folder, $"{i + 1:000} {names[i]}.flac");
                    File.WriteAllBytes(path, []);
                    entries.Add(new(new(path, LibraryPath.Key(path), 23_000_000 + i, 1), ScanEntryKind.Metadata,
                        new(names[i], index == 0 ? "ECHO Ensemble" : index == 1 ? "Coastal Letters" : "Live Sessions", album,
                            "ECHO Ensemble", 190 + i * 2, i % 3 == 0 ? "mp3" : "flac", 96000, 24, i + 1)));
                }
            }
            var direct = Path.Combine(root, "Opening.wav");
            File.WriteAllBytes(direct, []);
            entries.Add(new(new(direct, LibraryPath.Key(direct), 1_000_000, 1), ScanEntryKind.Metadata,
                new("Opening", "ECHO", "Singles", "ECHO", 60, "wav", 48000, 16)));
            var job = database.BeginScanAsync(root, false, budget.Token); Pump(job);
            long sequence = 0;
            foreach (var batch in entries.Chunk(200)) Pump(database.CommitScanBatchAsync(job.Result, ++sequence, batch, budget.Token));
            Pump(database.FinishScanAsync(job.Result, ScanStatus.Completed, entries.Count, false, null, budget.Token));
            var sibling = database.BeginScanAsync(root + "-extra", false, budget.Token); Pump(sibling);
            var extraPath = Path.Combine(root + "-extra", "outside.mp3");
            Pump(database.CommitScanBatchAsync(sibling.Result, 1, [new(new(extraPath, LibraryPath.Key(extraPath), 1, 1), ScanEntryKind.Metadata,
                new("Outside", "Outside", "Outside", "Outside", 30, "mp3", 44100, 0))], budget.Token));
            Pump(database.FinishScanAsync(sibling.Result, ScanStatus.Completed, 1, false, null, budget.Token));
            // Nested and repeated scans cannot evict original roots with scan history pruning.
            for (var i = 0; i < 33; i++)
            {
                var nested = database.BeginScanAsync(Path.Combine(root, albums[0]), false, budget.Token); Pump(nested);
                Pump(database.FinishScanAsync(nested.Result, ScanStatus.Completed, 0, false, null, budget.Token));
            }
            var roots = database.GetFoldersAsync(new(), budget.Token); Pump(roots);
            Require(roots.Result.Items.Count == 2 && roots.Result.Items.Single(row => row.Path == root).TrackCount == entries.Count, "persistent roots and path boundary");
            var pagedFolders = database.GetFoldersAsync(new(root, Limit: 2), budget.Token); Pump(pagedFolders);
            Require(pagedFolders.Result.HasMore && pagedFolders.Result.Items.Count == 2, "bounded child page");
            var first = database.GetFolderTracksAsync(new(root), budget.Token); Pump(first);
            Require(first.Result.Total == entries.Count && first.Result.Items.Count == 200, "bounded track page");
            var second = database.GetFolderTracksAsync(new(root, Offset: 200, Revision: first.Result.Revision), budget.Token); Pump(second);
            Require(!first.Result.Items.Select(row => row.Id).Intersect(second.Result.Items.Select(row => row.Id)).Any(), "stable pages");
            var local = database.GetFolderTracksAsync(new(root, Recursive: false), budget.Token); Pump(local);
            Require(local.Result.Total == 1 && local.Result.Items[0].Title == "Opening", "direct children only");
            Pump(database.SetTrackFavoriteAsync(local.Result.Items[0].Id, true, budget.Token));
            try
            { Pump(database.GetFolderTracksAsync(new(root, Offset: 200, Revision: first.Result.Revision), budget.Token)); throw new InvalidOperationException("Stale revision accepted."); }
            catch (LibraryChangedException) { }
            Pump(database.SetTrackFavoriteAsync(local.Result.Items[0].Id, false, budget.Token));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var aborted = database.GetFoldersAsync(new(root), cancelled.Token);
            try { Pump(aborted); throw new InvalidOperationException("Cancellation was ignored."); } catch (OperationCanceledException) { }

            window = DesktopComposition.CreateWindow(false, false, database, playback: runtime);
            var shell = (ShellViewModel)window.DataContext!;
            if (isolated)
            {
                var translations = new Dictionary<string, Dictionary<string, string>>();
                foreach (var code in new[] { "zh-CN", "en-US" })
                {
                    using var json = JsonDocument.Parse(File.ReadAllText($"src/Echo.Desktop/Resources/Languages/{code}.json"));
                    translations[code] = json.RootElement.GetProperty("strings").EnumerateObject()
                        .Where(pair => pair.Name.StartsWith("folders.", StringComparison.Ordinal)).ToDictionary(pair => pair.Name, pair => pair.Value.GetString()!);
                }
                previewLanguage = () => { foreach (var entry in translations[shell.Text.Current.Code == "en-US" ? "en-US" : "zh-CN"]) window.Resources[entry.Key] = entry.Value; };
                shell.Text.Changed += previewLanguage; previewLanguage();
            }
            shell.AttachLibrary(database, shell.AlbumArtwork!, (folder, progress, cancellation) =>
                new Echo.Application.Scanning.ScanCoordinator(database, new DirectoryDiscovery(),
                    () => new Echo.Infrastructure.Metadata.LocalMetadataReader()).ScanAsync(new(folder), progress, cancellation));
            shell.LastFolderPath = root;
            shell.Text.SetLanguage("zh-CN");
            shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
            window.Show(); Settle(); shell.NavigateCommand.Execute("folders"); Settle();
            if (isolated) MountFolderPage();
            var view = window.GetVisualDescendants().OfType<FoldersView>().Single(); var model = view.Model;
            Wait(() => !model.IsLoading && model.Total == entries.Count, "folder page startup");
            Require(model.Tracks.Count <= 200, "resident rows");
            var rootNode = model.Tree.Nodes.Single(node => node.Row.Path == root);
            var expand = view.GetVisualDescendants().OfType<Button>().First(button => button.DataContext == rootNode && button.Name == "ExpandFolder");
            Click(expand); Wait(() => rootNode.IsExpanded && model.Tree.Nodes.Count == 5, "native tree expand");
            // Read/play only this album, then return to the root for the wall preview.
            model.SelectedNode = model.Tree.Nodes.Single(node => node.Name == albums[0]);
            Wait(() => !model.IsLoading && model.Total == 9, "child selection");
            Pump(model.ActAsync());
            Require(runtime.LocalZone.Session.QueueState.Items.Count == 9 && runtime.LocalZone.Session.QueueState.Items[0].Origin?.Kind == "folder",
                $"folder playback and origin: queue={runtime.LocalZone.Session.QueueState.Items.Count}, loading={model.IsLoading}, busy={model.IsActing}, error={model.Error}");
            var cueRow = model.Tracks[1].Row;
            var cueSource = new TrackSource("cue", cueRow.Path, Path.Combine(root, "album.cue"), "fixture", 0, 1, 0, 1, StartSeconds: 10, EndSeconds: 30);
            Pump(model.ActAsync([cueRow with { Source = cueSource }]));
            Require(runtime.LocalZone.Session.QueueState.Items.Single().Source?.Range == new SourceRange(10, 30), "CUE source range");
            Pump(model.ActAsync());
            var favorite = model.Tracks[0]; Pump(favorite.ToggleFavoriteCommand.ExecuteAsync(null));
            var favoriteLookup = database.FindTrackByPathAsync(favorite.Row.Path, budget.Token); Pump(favoriteLookup);
            Require(favoriteLookup.Result?.Favorite == true, "favorite persistence");
            model.SelectedNode = rootNode; Wait(() => !model.IsLoading && model.Total == entries.Count, "return to root");
            Save("folders-light.png"); CheckAlignment(view);
            var tableTop = view.FindControl<FolderTrackList>("TrackPanel")!.TranslatePoint(default, window)!.Value.Y;
            Require(tableTop < 320, $"compact directory header: table starts at {tableTop}");
            Console.WriteLine($"Compact folder table top: {tableTop:0.#}px at 1440×900.");
            shell.IsDark = true; Settle(); Save("folders-dark.png");
            model.Search = "no-such-track"; model.Search = "Moonlight";
            Wait(() => !model.IsLoading && model.Total == 1, "latest search");
            model.Search = ""; model.LosslessOnly = true;
            Wait(() => !model.IsLoading && model.Total == 172, "lossless filtering");
            model.LosslessOnly = false; model.Recursive = false;
            Wait(() => !model.IsLoading && model.Total == 1, "recursive switch");
            model.Recursive = true; Wait(() => !model.IsLoading && model.Total == entries.Count, "filter restore");
            Pump(model.NextCommand.ExecuteAsync(null)); Require(model.Tracks.Count == 58 && model.CanPrevious, "next page");
            Pump(model.PreviousCommand.ExecuteAsync(null)); Require(model.Tracks.Count == 200, "previous page");
            var stableCover = view.FindControl<Image>("CoverFront")!.Source; var stableSummary = model.HeaderSummary;
            model.Search = "nothing-matches"; Wait(() => model.IsEmpty && !model.IsLoading, "empty state");
            Require(ReferenceEquals(stableCover, view.FindControl<Image>("CoverFront")!.Source) && model.HeaderSummary == stableSummary, "search preserves folder identity");
            Save("folders-empty.png");
            model.Search = ""; Wait(() => !model.IsLoading && model.Tracks.Count == 200, "search restore");
            var sortChoice = model.SortOptions[0]; var sortIndex = model.SortIndex;
            shell.Text.SetLanguage("en-US"); Settle();
            Require(ReferenceEquals(model.SortOptions[0], sortChoice) && sortChoice.Label == "File order" && model.SortIndex == sortIndex, "live sort localization keeps selection");
            Save("folders-english.png"); shell.Text.SetLanguage("zh-CN");
            window.Width = 980; Settle(); CheckAlignment(view); Save("folders-narrow.png");
            window.Width = 800; Settle(); CheckAlignment(view); Save("folders-800.png");
            var treeToggle = view.FindControl<Button>("DirectoryToggle")!;
            Require(treeToggle.IsVisible, "minimum window directory control");
            Click(treeToggle); Settle(); Require(view.FindControl<Grid>("TreePane")!.IsVisible, "compact directory open");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle();
            Require(!view.FindControl<Grid>("TreePane")!.IsVisible, "compact directory keyboard close");
            window.Width = 1440; Settle();
            view.SetWindowVisible(false); window.Hide(); Settle(); window.Show(); view.SetWindowVisible(true); Settle();
            shell.NavigateCommand.Execute("home"); Settle();
            Require(model.Tracks.Count == 0 && model.Tree.Nodes.Count == 0, "page release");
            shell.NavigateCommand.Execute("folders"); Settle();
            if (isolated) MountFolderPage();
            var restored = window.GetVisualDescendants().OfType<FoldersView>().Single();
            Wait(() => !restored.Model.IsLoading && restored.Model.CurrentFolder?.Path == root && restored.Model.Total == entries.Count, "navigation restore");
            Console.WriteLine("PASS folders: isolated SQLite, retained roots after 33 scans, lazy tree, bounded pages, path boundary, cancellation, filters/search, favorites, playback, native light/dark/narrow/English renders, column alignment and disposal. Audio simulated.");

            void Click(Button button)
            { var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value; window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle(); }
            void Save(string name)
            {
                window.MouseMove(new Point(window.Bounds.Width - 20, 50));
                // Let native theme-state transitions settle before accepting a screenshot.
                for (var frame = 0; frame < 5; frame++) { Settle(); Thread.Sleep(20); }
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
                bitmap.Render(window); bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); Console.WriteLine(Path.Combine(output, name));
            }
            void MountFolderPage()
            {
                var host = window.GetVisualDescendants().OfType<MotionPresenter>().Single(control => control.Name == "PageHost");
                host.Content = new FoldersView(shell) { Margin = new Thickness(42, 24, 42, 0) }; Settle();
            }
        }
        finally
        {
            if (previewLanguage is not null && window?.DataContext is ShellViewModel currentShell) currentShell.Text.Changed -= previewLanguage;
            // The isolated harness mounts outside the precompiled shell's page field.
            // It owns that page and must return leases before the shell disposes its cache.
            if (isolated && window is not null)
                foreach (var page in window.GetVisualDescendants().OfType<FoldersView>()) page.Dispose();
            if (window is not null) { window.Close(); Wait(() => !window.IsVisible, "window release"); }
            Pump(runtime.DisposeAsync().AsTask()); Pump(database.DisposeAsync().AsTask());
        }
        void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Wait(Func<bool> ready, string reason)
        { while (!ready()) { budget.Token.ThrowIfCancellationRequested(); Settle(); Thread.Sleep(2); } Settle(); }
        void Pump(Task task) { Wait(() => task.IsCompleted, "task completion"); task.GetAwaiter().GetResult(); }
    }
    private static void CheckAlignment(FoldersView view)
    {
        var header = view.GetVisualDescendants().OfType<FolderColumnsGrid>().Single(grid => grid.Name == "HeaderColumns");
        var row = view.GetVisualDescendants().OfType<FolderColumnsGrid>().First(grid => grid.Name is null);
        foreach (var a in header.Children)
        {
            var b = row.Children.First(control => Grid.GetColumn(control) == Grid.GetColumn(a));
            if (!a.IsVisible) continue;
            var headerX = a.TranslatePoint(default, view)!.Value.X; var rowX = b.TranslatePoint(default, view)!.Value.X;
            if (a.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right && b.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right)
            { headerX += a.Bounds.Width; rowX += b.Bounds.Width; }
            Require(Math.Abs(headerX - rowX) < 1, $"header/row alignment: column={Grid.GetColumn(a)}, header={headerX}, row={rowX}, widths={header.Bounds.Width}/{row.Bounds.Width}");
        }
    }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static void Cover(string folder, int index)
    {
        string[] colors = ["#253f69", "#477c88", "#704251"];
        using var bitmap = new SKBitmap(256, 256); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColor.Parse(colors[index]));
        using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#d1d7e2"), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
        for (var i = 0; i < 9; i++) canvas.DrawCircle(128, 128, 20 + i * 12, paint);
        paint.Style = SKPaintStyle.Fill; canvas.DrawCircle(128, 128, 18, paint);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(folder, "cover.png")); data.SaveTo(file);
    }
    private sealed class PreviewNative : INativeAudio
    {
        private long _operation;
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            var json = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openFile" or "audio.seek" => $"{{\"operationId\":{++_operation},\"sampleRate\":48000,\"durationSeconds\":223}}", _ => "true"
            };
            using var document = JsonDocument.Parse(json); return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
