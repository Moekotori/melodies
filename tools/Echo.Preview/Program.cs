using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Shell;

// Native Avalonia render, no HTML recreation. This tool is not loaded by Desktop.
AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions()).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (args.Length == 1 && args[0] == "--desktop-lyrics")
{ Echo.Desktop.Features.Floating.DesktopLyricsLayout.Verify(); Console.WriteLine("PASS desktop lyrics layout"); return; }
if (args.Length >= 2 && args[0] == "--song-menu")
{ SongContextMenuSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--dsp")
{ DspWorkbenchSmoke.Run(Path.GetFullPath(args[1]), args.Length > 2 ? Path.GetFullPath(args[2]) : Path.Combine(AppContext.BaseDirectory, "native", "echo-audio-host.exe")); return; }
if (args.Length >= 2 && args[0] == "--dsp-appearance")
{ DspAppearanceCapture.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--queue-window")
{ QueueDrawerSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--settings-functional")
{ await SettingsFunctionalSmoke.RunAsync(Path.GetFullPath(args[1])).ConfigureAwait(false); return; }
if (args.Length >= 2 && args[0] == "--user-languages")
{ UserLanguagePanelSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 3 && args[0] == "--songs")
{ SongsVisualCapture.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); return; }
if (args.Length >= 2 && args[0] == "--songs-visual")
{ SongsVisualCapture.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--playlists")
{ PlaylistUiSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--theme")
{ ThemeUiSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 3 && args[0] == "--browse")
{ BrowseUiSmoke.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); return; }
if (args.Length >= 2 && args[0] == "--home")
{ HomeUiSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--song-drop")
{ SongDropSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--global-search")
{ GlobalSearchSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--genres")
{ GenreUiSmoke.Run(Path.GetFullPath(args[1]), args.Length > 2 ? args[2] : null); return; }
if (args.Length >= 2 && args[0] == "--shortcuts")
{ ShortcutSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--history")
{ HistorySmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 3 && args[0] == "--queue-reference")
{ QueueVisualCapture.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); return; }
if (args.Length >= 2 && args[0] == "--motion-depth")
{
    Directory.CreateDirectory(args[1]);
    try { CompositionSmoke.Run(args[1]); MotionDepthSmoke.Run(args[1]); }
    catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    return;
}
if (args.Length >= 3 && args[0] == "--search")
{ LibrarySearchUiSmoke.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); return; }
if (args.Length >= 2 && args[0] == "--details")
{ DetailUiSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--folders")
{ FolderUiSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--i18n")
{ LocalizationSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--playerbar")
{ PlayerBarSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--floating")
{ FloatingWindowsSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--quick-settings")
{ QuickSettingsSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--queue")
{ QueueUiSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 2 && args[0] == "--settings")
{ await SettingsSmoke.RunAsync(Path.GetFullPath(args[1])).ConfigureAwait(false); return; }
if (args.Length >= 2 && args[0] == "--sidebar")
{ SidebarDrawerSmoke.Run(Path.GetFullPath(args[1])); return; }
if (args.Length >= 3 && args[0] == "--albums")
{ AlbumWallSmoke.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); return; }
if (args.Length >= 3 && args[0] == "--artists")
{ ArtistWallSmoke.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); return; }
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/previews");
Directory.CreateDirectory(output);
using var windowLifetime = new PreviewLifetime();
var window = windowLifetime.Window;
var model = (ShellViewModel)window.DataContext!;
window.Show();
Dispatcher.UIThread.RunJobs();
CompositionSmoke.Run(output);
MotionSmoke.Run(window, model, output);
// Capture settled appearance; motion itself is checked above.
var captureMotion = model.Motion.SelectedChoice;
model.Motion.SelectedChoice = model.Motion.Choices.First(choice => choice.Mode == Echo.Desktop.Motion.MotionMode.Off);
foreach (var dark in new[] { false, true })
{
    model.IsDark = dark;
    window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    Dispatcher.UIThread.RunJobs();
    using var bitmap = new RenderTargetBitmap(new PixelSize(1440, 900), new Vector(96, 96));
    bitmap.Render(window);
    var path = Path.Combine(output, dark ? "home-dark.png" : "home-light.png");
    bitmap.Save(path, PngBitmapEncoderOptions.Default);
    Console.WriteLine(path);
}
model.Motion.SelectedChoice = captureMotion;

// Exercise actual bound commands and view lifecycle, without playback simulation.
model.SetRecentTabCommand.Execute("added");
if (model.RecentEmptyTitle != "还没有最近入库") throw new InvalidOperationException("Recent tab did not update.");
model.NavigateCommand.Execute("songs");
if (model.IsHome || model.ActivePageTitle != "曲目" || !model.LibraryItems[1].IsActive) throw new InvalidOperationException("Navigation did not update.");
model.ToggleSidebarCommand.Execute(null);
model.NavigateCommand.Execute("home");
Dispatcher.UIThread.RunJobs();
if (model.IsSidebarExpanded || !model.IsHome) throw new InvalidOperationException("Sidebar state did not survive navigation.");
Console.WriteLine("Native render, navigation, theme, tab and sidebar checks passed.");

sealed class PreviewLifetime : IDisposable
{
    public MainWindow Window { get; } = DesktopComposition.CreateWindow(restorePlayback: false, enableMedia: false);
    public void Dispose() => Window.Close();
}
