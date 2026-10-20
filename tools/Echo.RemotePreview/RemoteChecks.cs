using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Application.Remote;
using Echo.Contracts.Remote;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Remote;
using Echo.Desktop.Shell;
using Echo.Desktop.Localization;
using Echo.Infrastructure.Remote;
using Echo.Platform.Windows;
using Echo.Playback;

internal static class RemoteChecks
{
    public static void Run(string output, string? languageRoot = null)
    {
        Directory.CreateDirectory(output); var clock = Stopwatch.StartNew(); var checks = 0;
        var fixture = new RemoteFixture(); using var files = new WebDavFileClient(fixture);
        var store = new MemoryConnections(); var catalog = new RemoteSourceCatalog(store, files);
        // Synthetic credentials exist only in memory, never written into fixture files or logs.
        var connection = new RemoteConnection("nas", "音乐 NAS", "https://music.example.com/dav/", "fixture", "memory-only");
        Pump(catalog.SaveAsync(connection, default)); Check(catalog.Sources.Count == 1 && fixture.AuthenticationSeen, "Confirmed source saved");
        var page = Result(files.BrowseAsync(connection, "/", 0, "", false, default));
        Check(page.Total == 424 && page.Entries.Count == 200 && page.Entries[0].IsDirectory, "Pagination / folder order / unsafe href exclusion");
        var last = Result(files.BrowseAsync(connection, "/", 2, "", false, default));
        Check(last.Entries.Count == 24 && last.Entries.Any(x => x.Name == "星光.flac"), "Unicode and last page");
        Check(Result(files.BrowseAsync(connection, "/", 0, "", true, default)).Total == 423, "Audio filter preserves folders");
        Check(Result(files.BrowseAsync(connection, "/", 0, "Track 007", false, default)).Total == 1, "Search is applied before paging");
        Check(files.GetMediaUrl(connection, "/星光.flac").EndsWith("%E6%98%9F%E5%85%89.flac"), "Encoded URL resolution");
        Reject(() => files.GetMediaUrl(connection, "/../outside.flac"), "Traversal rejected");
        fixture.Unauthorized = true; RejectTask(catalog.SaveAsync(connection with { Name = "Bad edit" }, default), "401 blocks save"); fixture.Unauthorized = false;
        Check(catalog.Sources[0].Name == connection.Name, "Failed probe retains saved source");
        store.Fail = true; RejectTask(catalog.SaveAsync(connection with { Name = "Unconfirmed edit" }, default), "Failed store receipt blocks publish"); store.Fail = false;
        Check(catalog.Sources[0].Name == connection.Name, "Write failure retains source");
        fixture.Oversized = true; RejectTask(files.BrowseAsync(connection, "/", 0, "", false, default), "Oversized response rejected"); fixture.Oversized = false;
        fixture.Malformed = true; RejectTask(files.BrowseAsync(connection, "/", 0, "", false, default), "DTD rejected"); fixture.Malformed = false;
        if (OperatingSystem.IsWindows())
        {
            byte[] input = [1, 2, 3, 4]; var encrypted = WindowsSecretProtection.Protect(input);
            Check(!encrypted.SequenceEqual(input) && WindowsSecretProtection.Unprotect(encrypted).SequenceEqual(input), "Real in-memory platform protection");
        }
        LocalizationService? text = null;
        if (languageRoot is not null)
        {
            var packs = new[] { "zh-CN", "en-US" }.Select(code =>
            {
                using var stream = File.OpenRead(Path.Combine(languageRoot, code + ".json"));
                return LanguageCatalog.ReadPack(stream);
            }).ToArray();
            text = new LocalizationService(new LanguageCatalog(packs));
            Console.WriteLine("Using isolated committed language fixtures; live language edits are not validated here.");
        }
        var shell = new ShellViewModel(text); shell.AttachRemote(catalog, files);
        var native = new RemoteNative(); var session = new PlaybackSessionCoordinator(native);
        var player = new PlayerViewModel(new PlaybackZone("remote-preview", "本机", session), shell.Text); shell.AttachPlayback(player);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == Echo.Desktop.Motion.MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        try
        {
            window.Show(); shell.NavigateCommand.Execute("remote"); Settle();
            var view = window.GetVisualDescendants().OfType<RemoteView>().Single(); var model = view.Model;
            Until(() => !model.IsBusy); model.SelectedSource = model.Sources[0]; Until(() => !model.IsBusy); Settle();
            Check(model.Entries.Count == 200 && model.Total == 424, "Native page selects persisted source");
            RemoteMotionChecks.Run(window, view, shell, output);
            Check(view.FindControl<ListBox>("FileList")!.GetVisualDescendants().OfType<ListBoxItem>().Count() < 30, "Actual row virtualization");
            Check(view.FindControl<ComboBox>("SourcePicker")!.SelectedItem is RemoteSourceInfo, "Source picker binds selection");
            Save("remote-light.png"); shell.IsDark = true; Settle(); Save("remote-dark.png"); shell.IsDark = false;
            model.SelectedEntry = model.Entries.First(x => x.IsAudio); Settle(); Save("remote-row-actions.png"); model.SelectedEntry = null;
            Click(view.FindControl<Button>("SourceMenuButton")!); Settle();
            var menu = (MenuFlyout)view.FindControl<Button>("SourceMenuButton")!.Flyout!;
            Check(menu.Items.OfType<MenuItem>().All(x => x.Command is not null), "Actual source menu command bindings");
            Save("remote-source-menu.png"); menu.Hide();
            Click(view.FindControl<Button>("AddSourceButton")!); Settle(); Save("remote-editor.png");
            Check(model.ProviderPickerOpen && model.ProviderChoices.Count == 7, "All original ECHO provider choices");
            Save("remote-providers.png");
            foreach (var provider in new[] { "jellyfin", "emby", "subsonic", "smb", "sshfs", "onedrive" })
            {
                model.ChooseProviderCommand.Execute(provider); Settle(); Save("remote-connect-" + provider + ".png");
                Check(model.DraftProvider == provider && model.EditorOpen, "Provider-specific form: " + provider);
                model.CloseEditorCommand.Execute(null); model.AddCommand.Execute(null); Settle();
            }
            Click(view.GetVisualDescendants().OfType<Button>().Single(x => x.Tag as string == "webdav")); Settle(); Save("remote-editor.png");
            shell.IsDark = true; Settle(); Save("remote-editor-dark.png"); shell.IsDark = false;
            Check(model.EditorOpen, "Native add button opens editor");
            Check(view.GetVisualDescendants().OfType<TextBox>().Any(x => x.Name == "UrlField" && x.IsFocused), "Connection editor focuses URL input");
            model.DraftName = "Invalid"; model.DraftUrl = "file:///invalid"; Pump(model.SaveCommand.ExecuteAsync(null));
            Check(model.EditorOpen && model.EditorError.Length > 0 && catalog.Sources.Count == 1, "Native invalid editor retains form"); model.CloseEditorCommand.Execute(null);
            model.EditCommand.Execute(null); model.DraftName = "音乐 NAS"; Pump(model.SaveCommand.ExecuteAsync(null)); Until(() => !model.IsBusy);
            Check(!model.EditorOpen && model.SelectedSource?.Id == connection.Id && model.Entries.Count == 200, "Saved source automatically browses");
            Click(view.FindControl<Button>("AudioFilesButton")!); Until(() => !model.IsBusy); Check(model.AudioOnly && model.Total == 423, "Native audio tab");
            Click(view.FindControl<Button>("AllFilesButton")!); Until(() => !model.IsBusy); Check(!model.AudioOnly && model.Total == 424, "Native all-files tab");
            view.FindControl<TextBox>("SearchField")!.Text = "Track 007"; view.FindControl<TextBox>("SearchField")!.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Until(() => !model.IsBusy); Check(model.Entries.Count == 1, "Native search input and Enter");
            model.SelectedEntry = model.Entries[0]; Settle();
            Click(view.GetVisualDescendants().OfType<Button>().Single(x => x.Tag as string == "row-play")); Until(() => !model.IsBusy);
            Check(native.OpenedWithAuthentication && session.Snapshot.Current?.Origin?.Kind == "remote", "Existing playback owner receives authenticated source");
            Check(session.Snapshot.Status == PlaybackStatus.Loading, "Playback waits for native confirmation");
            Click(view.GetVisualDescendants().OfType<Button>().Single(x => x.Tag as string == "row-queue")); Until(() => !model.IsBusy); Check(session.Queue.Count == 2, "Native row queue action uses existing owner");
            Check(!Result(session.ExportCheckpointAsync()).Contains("authorization", StringComparison.OrdinalIgnoreCase), "No authentication in playback checkpoint");
            model.Search = ""; Pump(model.RefreshCommand.ExecuteAsync(null)); model.SelectedEntry = model.Entries.First(x => x.IsDirectory);
            Pump(model.OpenCommand.ExecuteAsync(null)); Check(model.CurrentPath != "/" && model.Entries.Count == 1, "Directory navigation");
            Check(model.Breadcrumbs.Count == 2 && model.Breadcrumbs[^1].IsCurrent, "Bounded directory breadcrumbs");
            Click(view.GetVisualDescendants().OfType<Button>().Single(x => x.CommandParameter as string == "/")); Until(() => !model.IsBusy);
            Check(model.CurrentPath == "/" && model.Entries.Count == 200, "Native breadcrumb navigation");
            fixture.Unauthorized = true; model.SelectedEntry = model.Entries.First(x => x.IsDirectory); Pump(model.OpenCommand.ExecuteAsync(null)); fixture.Unauthorized = false;
            Check(model.CurrentPath == "/" && model.Page == 0 && model.Entries.Count == 200 && model.Message.Length > 0, "Failed navigation retains matching directory and rows");
            Pump(model.NextCommand.ExecuteAsync(null)); Check(model.Page == 1 && model.Entries.Count == 200, "Next-page action");
            window.Width = 900; window.Height = 720; Settle(); Save("remote-compact.png");
            Check(!view.FindControl<TextBlock>("SourceAddressText")!.IsVisible && view.FindControl<ComboBox>("SourcePicker")!.Bounds.Width < 200, "Compact source header fits viewport");
            shell.Text.SetLanguage("en-US"); Settle(); Save("remote-english.png"); Check(model.PageLabel.Contains("items"), "Language update");
            window.Width = 800; Settle(); Save("remote-small-english.png");
            Check(view.FindControl<ComboBox>("SourcePicker")!.Bounds.Right < view.Bounds.Width - 60, "Minimum-width English header retains actions");
            fixture.Hold = true; var late = model.RefreshCommand.ExecuteAsync(null); Until(() => fixture.Pending > 0);
            model.CancelCommand.Execute(null); fixture.Release.TrySetResult(); Pump(late);
            Check(!model.IsBusy && model.Entries.Count == 200, "Cancelled result retains previous page"); fixture.Hold = false;
            model.RequestRemoveCommand.Execute(null); Settle();
            Check(model.ConfirmRemove, "Remove source opens confirmation");
            model.CancelRemoveCommand.Execute(null); Settle();
            Check(!model.ConfirmRemove && model.Sources.Count == 1, "Cancelling removal preserves source");
            model.RequestRemoveCommand.Execute(null); Pump(model.RemoveCommand.ExecuteAsync(null));
            Check(model.HasNoSources && store.Saved.Length == 0 && model.Entries.Count == 0, "Confirmed UI removal restores first-connection workspace");
            shell.NavigateCommand.Execute("home"); Settle(); Check(model.Entries.Count == 0 && model.Sources.Count == 0, "Navigation releases page data");
            var emptyCatalog = new RemoteSourceCatalog(null, files); shell.AttachRemote(emptyCatalog, files);
            shell.NavigateCommand.Execute("remote"); Settle(); var empty = window.GetVisualDescendants().OfType<RemoteView>().Single(); Until(() => !empty.Model.IsBusy);
            shell.Text.SetLanguage("zh-CN"); Settle(); Save("remote-empty.png"); Check(empty.Model.HasNoSources, "First connection empty state");
            Check(empty.GetVisualDescendants().OfType<Button>().Count(x => x.Tag is string id && RemoteProviders.All.Contains(id)) == 7, "First connection shows providers directly");
            empty.Model.ChooseProviderCommand.Execute("webdav"); Settle();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Settle(); Check(!empty.Model.EditorOpen, "Keyboard Escape closes connection editor");
            Check(empty.GetVisualDescendants().OfType<Button>().Single(x => x.Tag as string == "subsonic").IsFocused, "Empty-state editor restores provider focus");
            Pump(catalog.RemoveAsync(connection.Id, default)); Check(catalog.Sources.Count == 0 && store.Saved.Length == 0, "Removal persists empty catalog");
            Console.WriteLine($"PASS {checks} checks; actual Avalonia page, WebDAV HTTP fixtures, playback protocol fixture; {clock.Elapsed.TotalSeconds:0.0}s.");
        }
        finally { window.Close(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization(); Pump(session.DisposeAsync().AsTask()); }
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks++; }
        void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { checks++; return; } throw new InvalidOperationException(name); }
        void RejectTask(Task task, string name) { try { Pump(task); } catch (Exception e) when (e is IOException or ArgumentException) { checks++; return; } throw new InvalidOperationException(name); }
        T Result<T>(Task<T> task) { Pump(task); return task.GetAwaiter().GetResult(); }
        void Pump(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); Settle(); }
        void Until(Func<bool> condition) { while (!condition()) { if (clock.Elapsed.TotalSeconds > 25) throw new TimeoutException("Remote short check budget"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } }
        void Settle() { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
        void Save(string name)
        {
            // Allow real compositor commits after theme changes; three immediate ticks can capture a stale fade.
            for (var frameIndex = 0; frameIndex < 6; frameIndex++) { Settle(); Thread.Sleep(10); }
            if (name == "remote-light.png")
            {
                var ancestors = window.GetVisualDescendants().OfType<RemoteView>().Single().GetVisualAncestors().OfType<Control>().ToArray();
                Check(ancestors.All(control => Echo.Desktop.Motion.MotionLane.CurrentOpacity(control) > 0.99), "Browser restores full presentation opacity");
            }
            using var frame = window.CaptureRenderedFrame(); if (frame is null) throw new IOException("Missing rendered frame");
            frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
        void Click(Button button)
        {
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle();
        }
    }
}
