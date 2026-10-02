using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Settings.Runtime;
using Echo.Desktop.Shell;
using Echo.Playback;

internal static class NativeWindowSmoke
{
    public static void Run(string output)
    {
        AppBuilder.Configure<App>().UsePlatformDetect().With(DesktopFonts.CreateOptions()).SetupWithoutStarting();
        using var loop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Exception? failure = null;
        Dispatcher.UIThread.Post(async () =>
        {
            var audio = new AudioFixture(); var session = new PlaybackSessionCoordinator(audio);
            var shell = new ShellViewModel(); shell.Text.SetLanguage("zh-CN");
            var player = new PlayerViewModel(new PlaybackZone("local", "本机", session), shell.Text);
            shell.AttachPlayback(player); player.Details.AttachLibrary(new LibraryFixture());
            var window = new MainWindow { DataContext = shell, ShowActivated = false };
            using var settings = new SettingsFeatureRuntime(shell.FeaturePreferences);
            using var binding = new WindowTaskbarBinding(window, shell, settings, new(window));
            try
            {
                Directory.CreateDirectory(output);
                window.Show();
                Console.WriteLine($"Native window shown; taskbar ready={binding.IsReady}; feedback={shell.Settings.Feedback}");
                await Until(() => binding.IsReady);
                Console.WriteLine("Native toolbar accepted");
                var file = Path.Combine(output, "native-fixture.wav"); File.WriteAllBytes(file, [0]);
                var a = new QueueTrack("a", "a", file, Metadata: new Dictionary<string, string?> { ["title"] = "ECHO Taskbar Check", ["artist"] = "短检查" });
                await session.SetQueueAsync([a, a with { ItemId = "b", TrackId = "b" }], "a");
                await Started(); await Until(() => player.Details.CanFavorite);
                await Task.Delay(80);
                var handle = window.TryGetPlatformHandle()!.Handle;
                Click(2); await Until(() => session.Snapshot.Status == PlaybackStatus.Paused && !player.IsBusy); await Task.Delay(60);
                Click(2); await Until(() => session.Snapshot.Status == PlaybackStatus.Loading); await Started();
                Click(3); await Until(() => session.Snapshot.Current?.ItemId == "b"); await Started();
                Click(1); await Until(() => session.Snapshot.Current?.ItemId == "a"); await Started();
                await Until(() => player.Details.CanFavorite); await Task.Delay(80);
                Click(4); await Until(() => player.Details.IsFavorite);
                shell.Settings.Feedback = "";
                SendMessageW(handle, 0x323, 0, (nint)((32 << 16) | 32));
                var thumbnailDiagnostic = shell.Settings.Feedback;
                shell.Settings.Feedback = "";
                SendMessageW(handle, 0x326, 0, 0);
                var liveDiagnostic = shell.Settings.Feedback;
                shell.UltraLightEnabled = true; window.WindowState = Avalonia.Controls.WindowState.Minimized;
                await Until(() => !window.IsSurfaceResident);
                Click(5); await Until(() => window.IsSurfaceResident && shell.IsQueueDrawerOpen);
                File.WriteAllText(Path.Combine(output, "native-window-report.txt"),
                    $"PASS: actual Avalonia HWND; ITaskbarList3 toolbar accepted; real WndProc hook routed five commands; minimized Ultralight queue recovery. Audio/library are fixtures. Physical Explorer hover and audio devices not tested.\nSynthetic DWM thumbnail: {(thumbnailDiagnostic.Length == 0 ? "accepted" : thumbnailDiagnostic)}\nSynthetic DWM live preview: {(liveDiagnostic.Length == 0 ? "accepted" : liveDiagnostic)}");
                Console.WriteLine(File.ReadAllText(Path.Combine(output, "native-window-report.txt")));
                void Click(int id) => SendMessageW(handle, 0x111, (nint)((0x1800 << 16) | id), 0);
                async Task Started()
                {
                    await Until(() => session.Snapshot.OperationId > 0 && !player.IsBusy);
                    audio.Notify("audio.started", $"{{\"operationId\":{session.Snapshot.OperationId}}}");
                    await Until(() => session.Snapshot.Status == PlaybackStatus.Playing); await Task.Delay(50);
                }
            }
            catch (Exception error) { Console.Error.WriteLine($"{error}\nTaskbar diagnostic: {shell.Settings.Feedback}"); failure = error; }
            finally
            {
                binding.Dispose(); window.RequestExit(); player.Detach(); shell.Settings.Dispose(); shell.ReleaseLocalization();
                await session.DisposeAsync(); loop.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(loop.Token);
        if (failure is not null) throw failure;
    }
    private static async Task Until(Func<bool> done)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!done()) await Task.Delay(10, timeout.Token);
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
}
