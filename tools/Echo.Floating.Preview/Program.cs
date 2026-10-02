using Avalonia;
using Avalonia.Headless;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = true }).SetupWithoutStarting();
var renderOnly = args.Length > 0 && args[0] == "--render";
FloatingWindowsSmoke.Run(Path.GetFullPath(renderOnly ? args.Length > 1 ? args[1] : "misc/floating/preview"
    : args.Length == 0 ? "misc/floating/preview" : args[0]), renderOnly);
