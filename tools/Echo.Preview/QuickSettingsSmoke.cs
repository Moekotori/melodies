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
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Settings.Drawers;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Lyrics;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class QuickSettingsSmoke
{
    private static readonly Stopwatch Budget = Stopwatch.StartNew();
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var session = new PlaybackSessionCoordinator(new NativeAudioHost(Path.Combine(AppContext.BaseDirectory, "native", "echo-audio-host.exe"), allowProDsp: true));
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session));
        var store = new LocalLyricsPreferencesStore(Path.Combine(output, "lyrics-settings.json"));
        var appearance = new LyricsAppearance(store);
        var shell = new ShellViewModel(); shell.AttachPlayback(player);
        // Only appearance is used by this drawer; no lyric documents or library service.
        shell.AttachLyrics(new EmptyLyricsSource(), appearance);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell };
        try
        {
            Require(Read(session.GetOutputModesAsync()).Count > 0, "native query host starts");
            window.Show(); Pump(40);
            var host = window.FindControl<QuickSettingsDrawer>("QuickSettingsOverlay")!;
            var titleBar = window.GetVisualDescendants().OfType<TitleBar>().Single();
            var moreButton = titleBar.FindControl<Button>("MoreButton")!;
            Click(moreButton); Pump(30);
            Save(window, output, "titlebar-more-light");
            var audioButton = titleBar.FindControl<MenuItem>("AudioDrawerButton")!;
            Click(audioButton); Pump(30);
            var audio = host.GetVisualDescendants().OfType<AudioSettingsDrawerView>().Single();
            Require(shell.ActiveRouteId == "home" && shell.QuickSettingsKind == "audio", "titlebar opens drawer without page navigation");
            Wait(player.AudioSettings.RefreshCommand.ExecutionTask!);
            Require(player.AudioSettings.Devices.Count > 0 && !player.AudioSettings.IsRefreshing, $"real native output enumeration: {player.AudioSettings.Message}; devices={player.AudioSettings.Devices.Count}, refreshing={player.AudioSettings.IsRefreshing}");
            Save(window, output, "audio-output-light");
            var devices = audio.FindControl<ListBox>("OutputDeviceInput")!;
            if (player.AudioSettings.Devices.Count > 1)
            {
                var second = devices.GetVisualDescendants().OfType<ListBoxItem>().ElementAt(1);
                var point = second.TranslatePoint(new Point(second.Bounds.Width / 2, second.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                Wait(player.AudioSettings.ApplyCommand.ExecutionTask!);
                Require(session.RequestedOutput!.Value.GetProperty("deviceId").GetString() == player.AudioSettings.Devices[1].Id, "native device row click applies selected output");
            }
            audio.FindControl<TabControl>("AudioTabs")!.SelectedIndex = 1; Pump(30);
            audio.FindControl<ComboBox>("SampleRateInput")!.SelectedItem = 44100;
            audio.FindControl<NumericUpDown>("BufferInput")!.Value = 512;
            // Configure effects independently, then ensure output edits cannot replay stale DSP values.
            Read(session.DspAsync("eq.setEnabled", Json("[true]"), true));
            Read(session.DspAsync("crossfeed.setState", Json("[{\"enabled\":true}]"), true));
            Wait(session.SetPlaybackRateAsync(1.25));
            Pump(30); Click(audio.FindControl<Button>("ApplyAudioButton")!);
            Wait(player.AudioSettings.ApplyCommand.ExecutionTask!);
            Require(session.RequestedOutput is { } config && config.GetProperty("sampleRate").GetInt32() == 44100
                && config.GetProperty("bufferSize").GetInt32() == 512, "bound settings reach session owner");
            var eq = Read(session.DspAsync("eq.getState", Json("[]")));
            var crossfeed = Read(session.DspAsync("crossfeed.getState", Json("[]")));
            Require(eq.GetProperty("enabled").GetBoolean() && crossfeed.GetProperty("enabled").GetBoolean() && session.Snapshot.PlaybackRate == 1.25,
                "output apply preserves independently configured EQ, crossfeed and speed");
            shell.IsDark = true; Pump(30); Save(window, output, "audio-settings-dark");
            var fresh = new PlayerAudioSettingsViewModel(session); Wait(fresh.RefreshCommand.ExecuteAsync(null));
            Require(fresh.SampleRate == 44100 && fresh.BufferFrames == 512, "acknowledged output reads back into a fresh settings VM");
            fresh.CancelPendingRead();
            audio.FindControl<TabControl>("AudioTabs")!.SelectedIndex = 2; Pump(30); Save(window, output, "audio-diagnostics-dark");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump(30);
            Require(!host.IsVisible && !host.IsKeyboardFocusWithin && host.FindControl<ContentControl>("DrawerContent")!.Content is null,
                "Escape releases the pointer-opened drawer and its focus");
            Click(moreButton); Pump(30);
            Save(window, output, "titlebar-more-dark");
            var lyricsButton = titleBar.FindControl<MenuItem>("LyricsDrawerButton")!;
            Click(lyricsButton); Pump(30);
            var lyrics = host.GetVisualDescendants().OfType<LyricsSettingsDrawerView>().Single();
            lyrics.FindControl<Slider>("LyricFontInput")!.Value = 42;
            lyrics.FindControl<Slider>("LyricFontInput")!.Focus(NavigationMethod.Tab);
            window.KeyPress(Key.Right, RawInputModifiers.None, default, null); Pump(20);
            Require(appearance.FontSize > 42, "custom lyric slider keeps keyboard range input");
            lyrics.FindControl<Slider>("LyricFontInput")!.Value = 42;
            lyrics.FindControl<CheckBox>("LyricTranslationInput")!.IsChecked = false;
            lyrics.FindControl<TabControl>("LyricsTabs")!.SelectedIndex = 2; Pump(30);
            lyrics.FindControl<NumericUpDown>("LyricOffsetInput")!.Value = 300;
            lyrics.FindControl<TabControl>("LyricsTabs")!.SelectedIndex = 1; Pump(30);
            lyrics.FindControl<ComboBox>("LyricStyleInput")!.SelectedItem = appearance.StyleChoices[2];
            Pump(30); Require(appearance.FontSize == 42 && !appearance.Translation && appearance.OffsetMs == 300 && appearance.SelectedStyle == appearance.Styles[2], "lyric bindings change shared live appearance");
            host.FindControl<TextBox>("DrawerSearchInput")!.Text = "时间偏移"; Pump(30);
            Require(lyrics.FindControl<TabControl>("LyricsTabs")!.SelectedIndex == 2, "drawer search selects matching setting category");
            host.FindControl<TextBox>("DrawerSearchInput")!.Text = ""; lyrics.FindControl<TabControl>("LyricsTabs")!.SelectedIndex = 0; Pump(30);
            shell.IsDark = false; Pump(30); Save(window, output, "lyrics-settings-light");
            window.MouseDown(new Point(100, 200), MouseButton.Left); window.MouseUp(new Point(100, 200), MouseButton.Left); Pump(30);
            Require(!shell.IsQuickSettingsOpen, "scrim dismisses drawer");
            foreach (var mode in new[] { MotionMode.Full, MotionMode.Reduced })
            {
                shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == mode);
                shell.OpenQuickSettingsCommand.Execute("lyrics"); Pump(40);
                shell.CloseQuickSettingsCommand.Execute(null); Pump(25);
                Require(!host.IsHitTestVisible, "exit immediately disables overlay input");
                shell.OpenQuickSettingsCommand.Execute("lyrics"); Pump(420);
                Require(host.IsVisible && !host.IsTransitioning, "reversal settles latest drawer state");
                window.Hide(); Pump(30); Require(!host.IsVisible && !shell.IsQuickSettingsOpen, "hidden window releases overlay");
                window.Show(); Pump(30);
            }
            shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
            shell.NavigateCommand.Execute("lyrics"); Pump(30);
            var stage = window.GetVisualDescendants().OfType<LyricsView>().Single();
            Click(stage.FindControl<Button>("SettingsButton")!); Pump(30);
            Require(host.IsEffectivelyVisible && !window.FindControl<LayoutTransformControl>("UiScaleHost")!.IsVisible, "quick drawer remains visible over standalone lyrics stage");
            Save(window, output, "lyrics-stage-drawer");
            shell.CloseQuickSettingsCommand.Execute(null); shell.NavigateCommand.Execute("home"); Pump(30);
            window.Width = 800; shell.UiScalePercent = 150; shell.OpenQuickSettingsCommand.Execute("lyrics"); Pump(40);
            Require(host.FindControl<Border>("DrawerPanel")!.Bounds.Width <= host.Bounds.Width - 12.0 + 0.01, "drawer fits scaled narrow viewport");
            Save(window, output, "lyrics-settings-narrow");
            shell.CloseQuickSettingsCommand.Execute(null);
            window.Height = 640; shell.OpenQuickSettingsCommand.Execute("audio"); Pump(40);
            Wait(player.AudioSettings.RefreshCommand.ExecutionTask!);
            audio = host.GetVisualDescendants().OfType<AudioSettingsDrawerView>().Single();
            audio.FindControl<TabControl>("AudioTabs")!.SelectedIndex = 1; Pump(30);
            Save(window, output, "audio-settings-narrow");
            host.FindControl<TextBox>("DrawerSearchInput")!.Text = "EQ"; Pump(30);
            Click(audio.FindControl<Button>("OpenDspButton")!); Pump(30);
            Require(shell.ActiveRouteId == "dsp" && !shell.IsQuickSettingsOpen, "DSP shortcut closes drawer and opens workbench");
            Wait(player.AudioSettings.ResetCommand.ExecuteAsync(null));
            Require(session.RequestedOutput!.Value.GetProperty("sampleRate").GetInt32() == 48000, "reset applies normal output");
            eq = Read(session.DspAsync("eq.getState", Json("[]")));
            crossfeed = Read(session.DspAsync("crossfeed.getState", Json("[]")));
            Require(eq.GetProperty("enabled").GetBoolean() && crossfeed.GetProperty("enabled").GetBoolean() && session.Snapshot.PlaybackRate == 1.25,
                "reset output preserves DSP and speed");
            Wait(store.DisposeAsync().AsTask());
            var reader = new LocalLyricsPreferencesStore(Path.Combine(output, "lyrics-settings.json"));
            var restored = new LyricsAppearance(reader); Wait(restored.RestoreAsync(CancellationToken.None));
            Require(restored.Snapshot() == appearance.Snapshot(), "lyrics preferences persist actual drawer edits");
            Wait(reader.DisposeAsync().AsTask());
            Console.WriteLine("PASS native quick drawers: titlebar routes, output query/apply/readback/reset with DSP preserved, live lyric bindings/persistence, Escape focus, scrim, reversal, reduced/off/full, hide/restore and scaled viewport.");
        }
        finally { window.Close(); shell.Settings.Dispose(); player.Detach(); Wait(session.DisposeAsync().AsTask()); }
    }
    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
    private static T Read<T>(Task<T> task) { Wait(task); return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task) { while (!task.IsCompleted) Pump(3); task.GetAwaiter().GetResult(); Pump(5); }
    private static void Click(Control button)
    {
        var window = TopLevel.GetTopLevel(button) as Window ?? throw new InvalidOperationException("Detached button");
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Save(Window window, string output, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var image = window.CaptureRenderedFrame() ?? throw new IOException("No rendered image");
        image.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private static void Pump(int milliseconds)
    {
        var start = Stopwatch.StartNew();
        while (start.ElapsedMilliseconds < milliseconds)
        {
            if (Budget.Elapsed.TotalSeconds > 45) throw new TimeoutException("Quick drawer smoke exceeded 45 seconds.");
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(2);
        }
        Dispatcher.UIThread.RunJobs();
    }
    private sealed class EmptyLyricsSource : Echo.Contracts.Lyrics.ILyricsSource
    {
        public Task<Echo.Contracts.Lyrics.LyricsText?> ReadAsync(string audioPath, string? explicitPath, CancellationToken cancellation)
            => throw new NotSupportedException("Drawer smoke does not read lyric documents.");
    }
}

