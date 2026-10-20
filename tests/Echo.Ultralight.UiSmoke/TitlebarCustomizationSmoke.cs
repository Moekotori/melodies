using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Settings;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Preferences;

internal static class TitlebarCustomizationSmoke
{
    // Covers right-click hit regions, actual edit actions, shared close preference and disk round-trip.
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var shell = new ShellViewModel();
        shell.Text.SetLanguage("zh-CN");
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == Echo.Desktop.Motion.MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 800 };
        try
        {
            window.Show(); Pump();
            var bar = window.GetVisualDescendants().OfType<TitleBar>().Single();
            var brand = bar.FindControl<TextBlock>("BrandText")!;
            var close = bar.FindControl<Button>("CloseButton")!;
            RightClick(brand);
            Require(brand.ContextMenu?.IsOpen == true, "brand right-click opens menu");
            ((MenuItem)brand.ContextMenu!.Items[0]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            var editor = window.OwnedWindows.OfType<TitlebarTextDialog>().Single();
            var input = editor.FindControl<TextBox>("TitleTextInput")!;
            Require(input.IsFocused && input.SelectedText == "melodies", "editor focuses and selects existing text");
            input.Text = "夜航 · melodies";
            Save(editor, Path.Combine(output, "text-editor-light.png"));
            editor.RequestedThemeVariant = ThemeVariant.Dark; Pump();
            Save(editor, Path.Combine(output, "text-editor-dark.png"));
            editor.FindControl<Button>("SaveTextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(brand.Text == "夜航 · melodies", "save updates visible title");
            RightClick(brand);
            ((MenuItem)brand.ContextMenu.Items[0]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            editor = window.OwnedWindows.OfType<TitlebarTextDialog>().Single();
            editor.FindControl<TextBox>("TitleTextInput")!.Text = "cancelled";
            editor.FindControl<Button>("CancelTextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(brand.Text == "夜航 · melodies", "cancel preserves title");

            RightClick(close);
            Require(close.ContextMenu?.IsOpen == true && window.IsVisible && !window.IsExitRequested,
                "close right-click opens options without closing");
            Save(close.ContextMenu!, Path.Combine(output, "close-options.png"));
            var direct = (MenuItem)close.ContextMenu!.Items[0]!;
            var background = (MenuItem)close.ContextMenu.Items[1]!;
            background.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Require(shell.CloseToTray && background.IsChecked && !direct.IsChecked, "background choice is exclusive");
            close.ContextMenu.Close();
            window.Close(); Pump();
            Require(!window.IsExitRequested && window.WindowState == WindowState.Minimized && window.ShowInTaskbar,
                "missing native tray retains taskbar recovery");
            window.RestoreMainWindow(); Pump();

            shell.TitlebarText = "  一行\r\n文字\t  ";
            Require(shell.TitlebarText == "一行  文字", "title normalizes pasted line breaks");
            shell.TitlebarText = new string('长', 90); Pump();
            Require(shell.TitlebarText.Length == 80 && brand.Bounds.Width <= 220, "long titles are bounded and clipped");
            Save(bar, Path.Combine(output, "long-title.png"));
            shell.SidebarAutoHideEnabled = true; window.Width = 640; Pump();
            var search = bar.FindControl<Button>("GlobalSearchButton")!;
            Require(search.TranslatePoint(default, bar)!.Value.X >= brand.TranslatePoint(default, bar)!.Value.X + brand.Bounds.Width,
                "long title does not overlap search in a narrow layout");
            window.Width = 800; shell.SidebarAutoHideEnabled = false; Pump();
            shell.TitlebarText = "夜航 · melodies";

            var path = Path.Combine(output, Guid.NewGuid().ToString("N") + ".json");
            var store = new JsonDesktopPreferencesStore(path);
            store.Save(new DesktopPreferences(Features: shell.FeaturePreferences.Snapshot()));
            Wait(store.DisposeAsync().AsTask());
            var read = new JsonDesktopPreferencesStore(path);
            var loading = read.ReadAsync(CancellationToken.None); Wait(loading);
            var restored = new ShellViewModel();
            restored.FeaturePreferences.Restore(loading.Result!.Features);
            Require(restored.TitlebarText == "夜航 · melodies" && restored.CloseToTray, "text and close choice survive disk round-trip");
            restored.Settings.Dispose(); restored.ReleaseLocalization();
            Wait(read.DisposeAsync().AsTask());

            RightClick(brand);
            ((MenuItem)brand.ContextMenu.Items[1]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Require(brand.Text == "melodies", "restore resets visible title");
            RightClick(close);
            direct.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Require(shell.IsDirectClose && direct.IsChecked && !background.IsChecked, "direct choice is exclusive");
            close.ContextMenu.Close();
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(window.IsExitRequested && !window.IsVisible, "direct choice exits instead of minimizing or prompting");
            Console.WriteLine("PASS titlebar context menus, editor save/cancel/reset, bounded text, local persistence, exclusive close policy, taskbar recovery and direct exit.");
        }
        finally
        {
            window.RequestExit(); shell.Settings.Dispose(); shell.ReleaseLocalization();
        }
    }

    private static void RightClick(Control target)
    {
        var window = (Window)TopLevel.GetTopLevel(target)!;
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Pump();
    }
    private static void Save(Control control, string path)
    {
        Pump();
        using var image = new RenderTargetBitmap(PixelSize.FromSize(control.Bounds.Size, 2), new Vector(192, 192));
        image.Render(control); image.Save(path, PngBitmapEncoderOptions.Default);
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Wait(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted)
        { Pump(); Thread.Sleep(5); if (timer.Elapsed.TotalSeconds > 5) throw new TimeoutException("Titlebar preference I/O"); }
        task.GetAwaiter().GetResult(); Pump();
    }
}
