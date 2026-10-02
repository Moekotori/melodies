using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Settings.Controls;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Preferences;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class SettingsSmoke
{
    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var session = new PlaybackSessionCoordinator(new NativeAudioHost(Path.Combine(AppContext.BaseDirectory, "native", "echo-audio-host.exe")));
        var player = new PlayerViewModel(new PlaybackZone(PlaybackZone.LocalId, "本机", session));
        var shell = new ShellViewModel(); shell.AttachPlayback(player);
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show(); shell.NavigateCommand.Execute("settings"); Pump();
            Save(window, output, "settings-general-light.png");
            foreach (var category in shell.Settings.NavigationGroups.SelectMany(group => group.Items))
            {
                shell.Settings.SelectCategoryCommand.Execute(category.Id); Pump();
                if (!window.GetVisualDescendants().OfType<SettingRowView>().Any()) throw new InvalidOperationException("Settings category has no rendered controls: " + category.Id);
            }
            shell.Settings.SelectCategoryCommand.Execute("general"); Pump();
            var collapse = window.GetVisualDescendants().OfType<ToggleButton>().First(control => AutomationProperties.GetName(control) == "侧栏仅显示图标");
            collapse.IsChecked = true; Pump();
            if (shell.IsSidebarExpanded) throw new InvalidOperationException("Sidebar setting binding failed.");
            collapse.IsChecked = false; Pump();
            shell.Settings.Sections.SelectMany(section => section.Rows).First(row => row.Id == "ui-scale").IncreaseCommand.Execute(null); Pump();
            if (shell.UiScalePercent != 105) throw new InvalidOperationException("Scale setting did not update its actual owner.");
            shell.Settings.Sections.SelectMany(section => section.Rows).First(row => row.Id == "ui-scale").ResetScaleCommand.Execute(null); Pump();
            shell.Settings.Query = "侧栏";
            var match = shell.Settings.SearchResults.First(result => result.RowId == "sidebar-icon-only");
            shell.Settings.OpenResultCommand.Execute(match); Pump();
            if (shell.Settings.Query.Length != 0 || !shell.Settings.Sections.SelectMany(section => section.Rows).Any(row => row.IsHighlighted))
                throw new InvalidOperationException("Settings search jump failed.");
            shell.Settings.SelectCategoryCommand.Execute("appearance"); Pump();
            shell.Theme.Mode = "深色"; Pump();
            if (!shell.IsDark) throw new InvalidOperationException("Theme choice did not change the window theme.");
            Save(window, output, "settings-appearance-dark.png");
            shell.IsDark = false; shell.Settings.SelectCategoryCommand.Execute("appearance"); Pump();
            Save(window, output, "settings-appearance-light.png");
            shell.Settings.SelectCategoryCommand.Execute("general"); shell.IsDark = true; Pump();
            Save(window, output, "settings-general-dark.png");
            shell.Settings.SelectCategoryCommand.Execute("playback"); Pump(); Save(window, output, "settings-playback-dark.png");
            shell.Settings.BackCommand.Execute(null); Pump();
            if (shell.ActiveRouteId != "home") throw new InvalidOperationException("Settings did not return to the previous page.");
            Console.WriteLine("Settings: all 13 categories render; actual toggle binding, scale, search jump, theme and back navigation passed.");
        }
        finally { window.Close(); shell.Settings.Dispose(); player.Detach(); }
        await session.DisposeAsync().ConfigureAwait(false);
        await VerifyPreferencesAsync(Path.Combine(output, "preferences-smoke.json")).ConfigureAwait(false);
    }
    private static async Task VerifyPreferencesAsync(string path)
    {
        var expected = new DesktopPreferences(UiScalePercent: 115, IsDark: true, SidebarIconOnly: true, MotionMode: "Reduced");
        var store = new JsonDesktopPreferencesStore(path);
        for (var scale = 75; scale <= 150; scale += 5) store.Save(expected with { UiScalePercent = scale });
        store.Save(expected); await store.DisposeAsync().ConfigureAwait(false);
        await using var reader = new JsonDesktopPreferencesStore(path);
        if (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false) != expected)
            throw new InvalidOperationException("Preferences round trip or latest pending save failed.");
        Console.WriteLine("Preferences: latest pending snapshot persisted and restored in an isolated file.");
    }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Save(MainWindow window, string output, string name)
    {
        Pump(); using var bitmap = new RenderTargetBitmap(new PixelSize(1440, 900), new Vector(96, 96));
        bitmap.Render(window); var path = Path.Combine(output, name); bitmap.Save(path, PngBitmapEncoderOptions.Default); Console.WriteLine(path);
    }
}
