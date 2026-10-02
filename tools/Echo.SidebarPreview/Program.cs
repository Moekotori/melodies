using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Shell;
using Echo.Contracts.Settings;
using Echo.Infrastructure.Preferences;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (args.Length >= 2 && args[0] == "--icons") { IconCatalogCapture.Run(Path.GetFullPath(args[1])); return; }
var output = Path.GetFullPath(args.Length == 0 ? "misc/sidebar-edit/preview" : args[0]);
Directory.CreateDirectory(output);
var model = new ShellViewModel();
model.Motion.SelectedChoice = model.Motion.Choices.First(x => x.Mode == Echo.Desktop.Motion.MotionMode.Off);
var window = new MainWindow { DataContext = model };
window.Show(); Pump();
var sidebar = window.FindControl<SidebarHost>("NavigationSidebar")!;
Save("sidebar-normal-light");
var view = sidebar.GetVisualDescendants().OfType<Sidebar>().Single();
var border = view.GetVisualDescendants().OfType<Border>().First(x => x.ContextMenu is not null);
window.MouseDown(new Point(100, 100), MouseButton.Right); window.MouseUp(new Point(100, 100), MouseButton.Right); Pump();
Require(border.ContextMenu!.IsOpen, "right-click opens context menu");
var edit = border.ContextMenu.Items.OfType<MenuItem>().First();
edit.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump();
Require(model.IsSidebarEditing, "context menu enters editor");
Require(view.FindControl<Button>("ExitEditing")!.IsEffectivelyVisible, "exit control is shown");
Save("sidebar-edit-light");
var songs = model.LibraryItems.Single(x => x.Id == "songs");
var home = model.LibraryItems.Single(x => x.Id == "home");
Click(RowButton(songs, "nav")); Require(model.ActiveRouteId == "home", "editor row does not navigate");
Click(RowButton(songs, "sidebar-eye")); Require(songs.IsHidden && songs.IsShown, "eye hides row while keeping it editable");
Save("sidebar-edit-hidden");
Drag(songs, model.LibraryItems.Single(x => x.Id == "albums"));
Require(model.LibraryItems.IndexOf(songs) > model.LibraryItems.IndexOf(home) + 1, "native pointer drag reorders");
var previous = model.LibraryItems.Select(x => x.Id).ToArray();
Drag(songs, model.SourceItems[0]);
Require(previous.SequenceEqual(model.LibraryItems.Select(x => x.Id)), "drag cannot cross group");
var grip = RowButton(songs, "sidebar-handle"); grip.Focus();
var before = model.LibraryItems.IndexOf(songs);
window.KeyPress(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, null); window.KeyRelease(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, null); Pump();
Require(model.LibraryItems.IndexOf(songs) == before - 1, "keyboard reorder");
var preferences = new DesktopPreferences(SidebarLayout: model.SidebarLayout);
var path = Path.Combine(output, "preferences.json");
var writer = new JsonDesktopPreferencesStore(path); writer.Save(preferences); writer.DisposeAsync().AsTask().GetAwaiter().GetResult();
var reader = new JsonDesktopPreferencesStore(path);
var saved = reader.ReadAsync(CancellationToken.None).GetAwaiter().GetResult()!;
reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
var restored = new ShellViewModel(); restored.RestoreSidebarLayout(saved.SidebarLayout);
Require(restored.LibraryItems.Select(x => x.Id).SequenceEqual(model.LibraryItems.Select(x => x.Id)) && restored.LibraryItems.Single(x => x.Id == "songs").IsHidden, "JSON restores order and hidden ids");
restored.RestoreSidebarLayout(new(["songs", "songs", "missing"], ["missing"]));
Require(restored.LibraryItems[0].Id == "songs" && restored.LibraryItems.Count == 6, "restore tolerates duplicate, stale and missing ids");
window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
Require(!model.IsSidebarEditing && !songs.IsShown && !RowButton(songs, "nav").IsEffectivelyVisible, "Escape exits and hides row");
model.IsSidebarExpanded = false; model.EditSidebarCommand.Execute(null); Pump();
Require(sidebar.Width == 220 && model.IsSidebarLabelVisible, "collapsed editor shows full labels");
Click(view.FindControl<Button>("ExitEditing")!); Require(!model.IsSidebarEditing && sidebar.Width == 72, "exit restores collapsed sidebar");
model.IsSidebarExpanded = true; model.EditSidebarCommand.Execute(null); model.ResetSidebarLayoutCommand.Execute(null); Pump();
Require(!songs.IsHidden && model.LibraryItems[1] == songs, "reset restores visibility and default order");
model.IsDark = true; Pump(); Save("sidebar-edit-dark");
model.Text.SetLanguage("en-US"); Pump(); Save("sidebar-edit-english");
model.SidebarAutoHideEnabled = true; model.OpenSidebarDrawerCommand.Execute(null); Pump();
window.MouseMove(new Point(219, 800)); Pump(); Require(model.IsSidebarDrawerOpen, "editing drawer stays open");
window.Close();
restored.Settings.Dispose(); restored.ReleaseLocalization(); model.Settings.Dispose(); model.ReleaseLocalization();
Console.WriteLine("PASS: native right-click/Enter, eye toggle, grouped pointer drag, keyboard reorder, exit/Escape, icon-only restore, reset, JSON compatibility; light/dark/English PNGs.");

Button RowButton(NavigationItem item, string cls) => view.GetVisualDescendants().OfType<Button>().Single(x => x.DataContext == item && x.Classes.Contains(cls));
Point Center(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
void Click(Button button) { var point = Center(button); window.MouseMove(point); Pump(); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump(); }
void Drag(NavigationItem item, NavigationItem target)
{
    var start = Center(RowButton(item, "sidebar-handle")); var end = Center(RowButton(target, "nav"));
    window.MouseMove(start); Pump(); window.MouseDown(start, MouseButton.Left); window.MouseMove(new Point(start.X, start.Y + 8)); window.MouseMove(end); window.MouseUp(end, MouseButton.Left); Pump();
}
void Save(string name)
{
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using var frame = new RenderTargetBitmap(new PixelSize((int)sidebar.Bounds.Width, (int)sidebar.Bounds.Height), new Vector(96, 96));
    frame.Render(sidebar);
    frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
}
void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
