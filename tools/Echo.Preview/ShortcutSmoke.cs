using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Shortcuts;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Shortcuts;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Preferences;
using Echo.Platform.Windows;
using Echo.Playback;

internal static class ShortcutSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = new PlaybackSessionCoordinator(new AudioFixture());
        var shell = new ShellViewModel();
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session), shell.Text);
        shell.AttachPlayback(player);
        shell.Text.SetLanguage("zh-CN"); shell.Motion.SelectedChoice = shell.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell };
        var backend = new RegistrationFixture();
        var binding = new WindowShortcutBinding(window, shell.Shortcuts, backend, null,
            "应用内按键仅在窗口中生效。全局按键在后台也可使用，支持媒体键与鼠标侧键。");
        try
        {
            window.Show(); Pump(binding.RestoreAsync()); shell.NavigateCommand.Execute("settings");
            shell.Settings.SelectCategoryCommand.Execute("shortcuts"); Settle();
            var model = shell.Shortcuts;
            Require(model.Rows.Count <= ShortcutLimits.MaxActions && model.Rows.Count(x => x.Local.Enabled) == 2 && model.Rows.All(x => !x.Global.Enabled)
                && model.ResolveLocal("Space") == "playPause" && model.ResolveLocal("Ctrl+Q") == "openPlaybackQueue",
                $"legacy defaults: rows={model.Rows.Count}, local={model.Rows.Count(x => x.Local.Enabled)}, global={model.Rows.Count(x => x.Global.Enabled)}");
            var next = model.Rows.First(x => x.Action.Id == "nextTrack").Local;
            model.BeginRecording(next); Wait(() => model.RecordingReady && !model.IsApplying);
            Press(window, Key.Q, KeyModifiers.Control);
            Require(model.IsRecording && next.Message.Length > 0 && !shell.IsQueueDrawerOpen, "conflicting capture must not dispatch or replace");
            Press(window, Key.Escape); Wait(() => !model.IsApplying);
            Require(next.Accelerator == "Ctrl+J" && !next.Enabled, "cancel retains original binding");
            model.BeginRecording(next); Wait(() => model.RecordingReady && !model.IsApplying);
            Press(window, Key.J, KeyModifiers.Alt); Wait(() => !model.IsApplying);
            Require(next.Enabled && model.ResolveLocal("Alt+J") == "nextTrack", "recorded local binding");
            var queue = model.Rows.First(x => x.Action.Id == "openPlaybackQueue").Local;
            model.BeginRecording(queue); Wait(() => model.RecordingReady && !model.IsApplying);
            Press(window, Key.Q, KeyModifiers.Alt); Wait(() => !model.IsApplying);
            Press(window, Key.Q, KeyModifiers.Control); Require(!shell.IsQueueDrawerOpen, "old hardcoded shortcut removed");
            Press(window, Key.Q, KeyModifiers.Alt); Require(shell.IsQueueDrawerOpen, "new queue binding executes once");
            shell.CloseQueueDrawerCommand.Execute(null);
            var input = window.GetVisualDescendants().OfType<TextBox>().First(x => x.Name == "FilterInput");
            Press(input, Key.Q, KeyModifiers.Alt); Require(!shell.IsQueueDrawerOpen, "editing does not dispatch local shortcut");
            var global = model.Rows.First(x => x.Action.Id == "openPlaybackQueue").Global;
            model.BeginRecording(global); Wait(() => model.RecordingReady && !model.IsApplying);
            Press(window, Key.Q, KeyModifiers.Alt); Wait(() => !model.IsApplying);
            Require(global.Registered, "global registration acknowledgement");
            Press(window, Key.Q, KeyModifiers.Alt); Require(!shell.IsQueueDrawerOpen, "global/local chord must not double dispatch");
            backend.Fire("openPlaybackQueue"); Wait(() => shell.IsQueueDrawerOpen);
            shell.CloseQueueDrawerCommand.Execute(null);
            model.BeginRecording(next); Wait(() => model.RecordingReady && !model.IsApplying);
            Require(backend.Bindings.Count == 0 && !global.Registered, "recording removes all global registrations");
            Press(window, Key.Escape); Wait(() => !model.IsApplying);
            Require(global.Registered, "cancel resumes global registrations");
            backend.FailAction = global.Action; model.RetryCommand.Execute(null); Wait(() => !model.IsApplying);
            Require(!global.Registered && global.Message.Length > 0, "OS registration failure is visible");
            model.IssuesOnly = true; Require(model.VisibleRows.Any(x => x.Global == global), "issues filter includes registration failures");
            model.IssuesOnly = false; backend.FailAction = null; model.RetryCommand.Execute(null); Wait(() => !model.IsApplying);
            var lookup = Stopwatch.StartNew();
            for (var i = 0; i < 100000; i++) _ = model.ResolveLocal("Alt+Q");
            lookup.Stop(); Console.WriteLine($"Shortcut lookup model: 100000 indexed reads, {lookup.Elapsed.TotalMilliseconds:F2} ms; no legacy benchmark comparison.");
            Capture("shortcuts-light.png"); shell.IsDark = true; Capture("shortcuts-dark.png");
            window.Width = 900; window.Height = 800; Capture("shortcuts-narrow.png");
            window.Width = 1440; window.Height = 900; shell.Text.SetLanguage("en-US"); Capture("shortcuts-english.png");
            var snapshot = model.Snapshot();
            var store = new JsonShortcutProfileStore(Path.Combine(output, "shortcuts.json"));
            store.Save(snapshot); Pump(store.DisposeAsync().AsTask());
            var reader = new JsonShortcutProfileStore(Path.Combine(output, "shortcuts.json"));
            var read = reader.ReadAsync(budget.Token); Pump(read);
            Require(read.Result?.Bindings.SequenceEqual(snapshot.Bindings) == true, "independent profile roundtrip"); Pump(reader.DisposeAsync().AsTask());
            Console.WriteLine("Shortcuts: routed native UI capture, conflicts, cancellation, remap, editing guard, global deduplication, suspend/resume, failure/retry, profile roundtrip and four renders passed.");
        }
        finally
        {
            Pump(binding.DisposeAsync().AsTask()); window.Close(); shell.Settings.Dispose(); shell.ReleaseLocalization(); player.Detach(); Pump(session.DisposeAsync().AsTask());
        }
        if (OperatingSystem.IsWindows()) VerifyWindows(budget.Token);
        void Settle() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Wait(Func<bool> condition) { while (!condition()) { budget.Token.ThrowIfCancellationRequested(); Settle(); Thread.Sleep(1); } Settle(); }
        void Pump(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
        void Capture(string name)
        {
            Settle(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
            bitmap.Render(window); bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
    }
    private static void Press(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = target });
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers, Source = target });
        Dispatcher.UIThread.RunJobs();
    }
    private static void VerifyWindows(CancellationToken cancellation)
    {
        var first = new WindowsGlobalShortcuts(); var second = new WindowsGlobalShortcuts();
        var keyboard = new ShortcutBinding("playPause", ShortcutScope.Global, true, "Ctrl+Alt+F24");
        try
        {
            var owned = first.ApplyAsync([keyboard], cancellation).GetAwaiter().GetResult();
            Require(owned.Single().Registered, "Win32 registration");
            var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            first.Triggered += action => { if (action == "playPause") delivered.TrySetResult(); };
            SendKey(0x11, false); SendKey(0x12, false); SendKey(0x87, false);
            SendKey(0x87, true); SendKey(0x12, true); SendKey(0x11, true);
            delivered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellation).GetAwaiter().GetResult();
            var blocked = second.ApplyAsync([keyboard], cancellation).GetAwaiter().GetResult();
            Require(!blocked.Single().Registered, "Win32 detects actual reservation conflict");
            first.ApplyAsync([], cancellation).GetAwaiter().GetResult();
            var released = second.ApplyAsync([keyboard, new("nextTrack", ShortcutScope.Global, true, "MouseButton5")], cancellation).GetAwaiter().GetResult();
            Require(released.All(x => x.Registered), "Win32 releases reservation and installs mouse hook");
        }
        finally { first.DisposeAsync().AsTask().GetAwaiter().GetResult(); second.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        Console.WriteLine("Win32: real registration, synthetic OS keyboard dispatch, occupied chord, release/re-register and mouse-hook lifecycle passed; physical input not verified.");
    }
    private static void SendKey(byte key, bool release) => keybd_event(key, 0, release ? 2u : 0u, 0);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class RegistrationFixture : IGlobalShortcutBackend
    {
        public IReadOnlyList<ShortcutBinding> Bindings { get; private set; } = [];
        public string? FailAction { get; set; }
        public event Action<string>? Triggered;
        public void Fire(string action) => Triggered?.Invoke(action);
        public async Task<IReadOnlyList<ShortcutRegistration>> ApplyAsync(IReadOnlyList<ShortcutBinding> bindings, CancellationToken cancellation)
        { await Task.Yield(); Bindings = bindings; return bindings.Select(x => new ShortcutRegistration(x.Action, x.Accelerator!, x.Action != FailAction, x.Action == FailAction ? "occupied" : null)).ToArray(); }
        public ValueTask DisposeAsync() { Triggered = null; return ValueTask.CompletedTask; }
    }
    private sealed class AudioFixture : INativeAudio
    {
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        { using var json = JsonDocument.Parse("true"); return Task.FromResult(json.RootElement.Clone()); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
