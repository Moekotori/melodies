using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Application.Radio;
using Echo.Contracts.Radio;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Radio;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Preferences;
using Echo.Playback;

internal static class RadioUiSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var watch = Stopwatch.StartNew();
        var store = new JsonRadioStationStore(Path.Combine(output, "stations-" + Guid.NewGuid().ToString("N") + ".json"));
        var catalog = new RadioCatalog(store);
        var native = new PreviewNative();
        var session = new PlaybackSessionCoordinator(native);
        var shell = new ShellViewModel(); shell.AttachRadio(catalog);
        var player = new PlayerViewModel(new PlaybackZone("radio-preview", "本机", session), shell.Text);
        shell.AttachPlayback(player);
        var resolver = new PreviewResolver(); shell.AttachRadioPlayback(resolver);
        var directory = new PreviewDirectory(); shell.AttachRadioServices(directory, directory, session);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1680, Height = 1000 };
        try
        {
            Pump(catalog.LoadAsync(default));
            Pump(catalog.UpdateAsync(items => items.Append(new("zeno-preview", "Zeno", "https://stream.zeno.fm/preview-only", "Zeno 托管的二次元直播流，轻量备用源。", "—", "MP3")), default));
            window.Show(); shell.NavigateCommand.Execute("radio"); Settle();
            var view = window.GetVisualDescendants().OfType<RadioView>().Single(); var model = view.Model;
            Check(model.Rows.Count == 2 && model.Frequency == "276", "Radio page / original station identity");
            Save("radio-light.png"); shell.IsDark = true; Settle(); Save("radio-dark.png"); shell.IsDark = false;
            Pump(model.FavoriteAsync(model.Rows[1].Station)); Settle();
            Click(view.FindControl<Button>("FavoritesTab")!); Settle();
            Check(model.Rows.Count == 1 && model.Rows[0].Name == "Zeno", "Favorites tab binding");
            view.FindControl<TextBox>("SearchField")!.Text = "no-such-station"; Settle(); Check(model.IsEmpty, "Search empty state"); Save("radio-empty.png");
            view.FindControl<TextBox>("SearchField")!.Text = ""; model.ShowAllCommand.Execute(null); Settle();
            model.Edit(model.Rows[1].Station); Save("radio-editor.png"); model.DraftUrl = "file:///invalid";
            Pump(model.SaveEditorCommand.ExecuteAsync(null)); Check(model.EditorOpen && model.EditorError.Length > 0, "Invalid URL stays in editor");
            model.DraftUrl = "https://stream.zeno.fm/preview-only"; model.DraftName = "Zeno edited";
            Pump(model.SaveEditorCommand.ExecuteAsync(null)); Settle(); Check(!model.EditorOpen && model.Rows[1].Name == "Zeno edited", "Editor saves");
            var readback = new RadioCatalog(store); Pump(readback.LoadAsync(default));
            Check(readback.Stations[1].Favorite && readback.Stations[1].Name == "Zeno edited", "Favorite / edit persist");
            Check(RadioPlaylist.Parse(model.Export()).Count == 2, "M3U round trip");
            var first = model.Rows[0].Station;
            resolver.Wait = true; var connecting = model.ListenCommand.ExecuteAsync(null); Settle();
            Check(model.PlayLabel == "取消连接" && model.ListenCommand.CanExecute(null), "Connecting command remains cancellable");
            Click(view.FindControl<Button>("ListenButton")!); Pump(connecting);
            Check(session.Snapshot.Current is null && model.PlayLabel == "开始收听", "Actual cancel button must not start audio");
            resolver.Wait = false; resolver.Fail = true; Pump(model.PlayAsync(first));
            Check(model.Status == "连接失败" && model.PlayLabel == "重新连接" && model.Message.Length > 0, "Probe failure has a retry entry");
            resolver.Fail = false;
            Pump(model.PlayAsync(first)); Settle(); Check(session.Snapshot.Current?.Source?.Uri == RadioCatalog.EchoUrl, "Actual playback command uses HTTP source");
            Check(model.Status == "连接中…", "No false playing state before native confirmation");
            native.Emit("audio.started", $"{{\"operationId\":{session.Snapshot.OperationId}}}"); Settle();
            Pump(session.SetVolumeAsync(0.5)); Settle();
            Check(model.Status == "直播中" && model.CanEnter, "Confirmed playing state");
            Check(player.IsLiveStream && !player.CanSeek && player.DurationText == "直播", "Live player presentation disables seeking");
            Click(view.FindControl<Button>("EnterButton")!); Settle();
            Check(model.StageOpen, "Radio stage opens"); Save("radio-stage.png"); model.StageOpen = false;
            Click(window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CoverButton"));
            Check(model.StageOpen && shell.ActiveRouteId == "radio", "Player cover opens the active radio stage"); model.StageOpen = false;
            Pump(model.PlayAsync(first)); Settle(); Check(session.Snapshot.Status == PlaybackStatus.Stopped, "Radio stops through session owner");
            RadioDirectoryUiSmoke.Run(window, view, directory, catalog, output);
            Pump(catalog.UpdateAsync(items => items.Where(item => !item.Url.StartsWith("https://radio.test/", StringComparison.Ordinal)), default));
            window.Width = 900; window.Height = 720; Settle(); Save("radio-compact.png");
            Check(view.FindControl<ItemsControl>("StationList")!.Bounds.Height >= 56, "Compact list retains a visible station row");
            shell.NavigateCommand.Execute("home"); Settle(); Check(model.Rows.Count == 0, "Navigation disposes radio page");
            Pump(catalog.UpdateAsync(_ => [], default)); var empty = new RadioCatalog(store); Pump(empty.LoadAsync(default));
            Check(empty.Stations.Count == 0, "Deleted default does not return");
            Console.WriteLine("Radio native UI, filter, edit, URL validation, persistence, M3U and HTTP session command passed. Audio and network are simulated.");
        }
        finally { window.Close(); shell.ReleaseRadioPlayback(); shell.ReleaseRadioServices(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization(); Pump(session.DisposeAsync().AsTask()); }
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Click(Button button)
        {
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle();
        }
        void Pump(Task task)
        {
            while (!task.IsCompleted) { if (watch.Elapsed.TotalSeconds > 25) throw new TimeoutException("Radio short validation"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            task.GetAwaiter().GetResult(); Settle();
        }
        void Settle() { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); } AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Save(string name) { Settle(); using var image = window.CaptureRenderedFrame() ?? throw new IOException("Radio capture missing"); image.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default); Console.WriteLine(Path.Combine(output, name)); }
    }

    private sealed class PreviewNative : INativeAudio
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
                "audio.openSource" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":0}}",
                _ => "true"
            };
            using var doc = JsonDocument.Parse(json); return Task.FromResult(doc.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class PreviewResolver : IRadioStreamResolver
    {
        public bool Wait { get; set; }
        public bool Fail { get; set; }
        public async Task<RadioStreamInfo> ResolveAsync(string url, CancellationToken token)
        {
            if (Wait) await Task.Delay(Timeout.Infinite, token);
            if (Fail) throw new IOException("电台连接失败，测试重试入口。");
            return new(url, "AAC");
        }
    }
}
