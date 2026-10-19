using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Settings;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Desktop.Features.Settings.Controls;
using Echo.Infrastructure.Preferences;

internal static class SidebarDrawerSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var model = new ShellViewModel();
        var window = new MainWindow { DataContext = model };
        model.Motion.SelectedChoice = model.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        window.Show(); Pump(30);
        var host = window.FindControl<SidebarHost>("NavigationSidebar")!;
        var panel = host.FindControl<Border>("NavigationPanel")!;
        var row = model.Settings.Sections.SelectMany(section => section.Rows).Single(row => row.Id == "sidebar-auto-hide");
        Require(row.IsEnabled, "settings entry enabled");
        row.BoolValue = true; Pump(30);
        Require(model.SidebarReservedWidth == 0 && host.Width == 22 && !panel.IsVisible, "hidden sidebar releases page and input");
        window.MouseMove(new Point(2, 200)); Pump(30);
        Require(model.IsSidebarDrawerOpen && panel.IsVisible && model.IsSidebarLabelVisible, "left edge pointer opens labeled drawer");
        Save(window, output, "sidebar-light");
        window.MouseMove(new Point(700, 200)); Pump(30);
        Require(!model.IsSidebarDrawerOpen, "pointer leaving closes hover drawer");
        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null); Pump(30);
        Require(panel.IsKeyboardFocusWithin, "keyboard opening focuses navigation");
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump(30);
        Require(!model.IsSidebarDrawerOpen && host.FindControl<Button>("RevealButton")!.IsFocused, "Escape closes drawer and restores edge focus");
        host.ToggleFromKeyboard(); Pump(30);
        model.NavigateCommand.Execute("home"); Pump(30);
        Require(!model.IsSidebarDrawerOpen, "same route selection closes drawer");
        model.OpenSidebarDrawerCommand.Execute(null); Pump(30);
        window.MouseDown(new Point(700, 200), MouseButton.Left); window.MouseUp(new Point(700, 200), MouseButton.Left); Pump(30);
        Require(!model.IsSidebarDrawerOpen, "outside click dismisses drawer");
        model.OpenQuickSettingsCommand.Execute("lyrics"); Pump(30);
        window.MouseMove(new Point(2, 200)); Pump(30);
        Require(!model.IsSidebarDrawerOpen && model.IsQuickSettingsOpen, "hover edge does not open behind settings");
        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null); Pump(30);
        Require(model.IsSidebarDrawerOpen && !model.IsQuickSettingsOpen && panel.IsKeyboardFocusWithin, "keyboard sidebar replaces settings and owns focus");
        model.CloseSidebarDrawerCommand.Execute(null); Pump(30);
        model.IsSidebarExpanded = false;
        model.OpenSidebarDrawerCommand.Execute(null); Pump(30);
        Require(model.IsSidebarLabelVisible, "drawer overrides icon-only presentation");
        model.IsDark = true; Pump(30); Save(window, output, "sidebar-dark");
        model.HideSidebarInSettings = true; model.NavigateCommand.Execute("settings"); Pump(30);
        Require(!host.IsVisible && !model.IsSidebarDrawerMode, "settings focus suppresses drawer and edge");
        model.Settings.BackCommand.Execute(null); Pump(30);
        Require(host.IsVisible && model.IsSidebarDrawerMode, "leaving settings restores drawer preference");
        model.SidebarAutoHideEnabled = false; Pump(30);
        Require(host.Width == 72 && model.SidebarReservedWidth == 72 && !model.IsSidebarLabelVisible, "icon-only preference restored");
        model.SidebarAutoHideEnabled = true;
        model.ToggleSidebarCommand.Execute(null); Pump(30);
        Require(!model.SidebarAutoHideEnabled && model.IsSidebarExpanded && host.Width == 220, "sidebar collapse control exits drawer mode");
        model.SidebarAutoHideEnabled = true;
        foreach (var mode in new[] { MotionMode.Full, MotionMode.Reduced })
        {
            model.Motion.SelectedChoice = model.Motion.Choices.First(x => x.Mode == mode);
            model.OpenSidebarDrawerCommand.Execute(null); Pump(40);
            model.CloseSidebarDrawerCommand.Execute(null); Pump(30);
            Require(!panel.IsHitTestVisible && !panel.IsEnabled, "retiring drawer cannot receive input");
            model.OpenSidebarDrawerCommand.Execute(null); Pump(420);
            Require(panel.IsVisible && panel.Opacity == 1 && panel.RenderTransform!.Value == Matrix.Identity, "reversal settles");
            window.Hide(); Pump(30);
            Require(!model.IsSidebarDrawerOpen && !panel.IsVisible, "window hiding closes drawer");
            window.Show(); Pump(30);
        }
        model.Motion.SelectedChoice = model.Motion.Choices.First(x => x.Mode == MotionMode.Off);
        model.NavigateCommand.Execute("settings"); model.HideSidebarInSettings = false; model.IsDark = false; Pump(30);
        var settingsScroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(scroll => scroll.Content is SettingsSectionView);
        settingsScroll.Offset = new Vector(0, 490); Pump(30);
        Save(window, output, "sidebar-settings");
        window.Width = 800; model.UiScalePercent = 150; model.OpenSidebarDrawerCommand.Execute(null); Pump(30);
        Require(host.Bounds.Width == 220, "drawer fits minimum window at maximum UI scale");
        Save(window, output, "sidebar-narrow");
        window.Close(); model.Settings.Dispose();
        var preferences = new DesktopPreferences(SidebarAutoHideEnabled: true, SidebarIconOnly: true, HideSidebarInSettings: true);
        var path = Path.Combine(output, "preferences.json");
        var store = new JsonDesktopPreferencesStore(path); store.Save(preferences);
        store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        var reader = new JsonDesktopPreferencesStore(path);
        Require(reader.ReadAsync(CancellationToken.None).GetAwaiter().GetResult() == preferences, "preferences roundtrip");
        reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
        File.WriteAllText(path, "{\"Version\":1,\"SidebarIconOnly\":true}");
        var legacy = new JsonDesktopPreferencesStore(path);
        Require(legacy.ReadAsync(CancellationToken.None).GetAwaiter().GetResult() is { SidebarAutoHideEnabled: false, SidebarIconOnly: true }, "pre-drawer preferences stay compatible");
        legacy.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine("PASS sidebar: bound setting, pointer edge/leave, keyboard focus/Escape, same route/outside dismissal, preference precedence, reversal, off/reduced/full modes, hide/restore, scale and JSON roundtrip.");
    }
    private static void Save(Window window, string output, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
        frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(2); }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
