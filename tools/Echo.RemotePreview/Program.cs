using Avalonia;
using Avalonia.Headless;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "misc/remote-validation/screenshots");
ProviderChecks.Run(Path.GetDirectoryName(output)!);
RemoteChecks.Run(output);
