using Avalonia;
using Avalonia.Headless;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;

// Focused native rendering entry point; no audio process or production database is opened.
AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (args.Length > 1 && args[0] == "--filter-reference") FilterReferenceCapture.Run(Path.GetFullPath(args[1]));
else SongsVisualCapture.Run(Path.GetFullPath(args.Length > 0 ? args[0] : "misc/songs-visual-refresh/previews"));
