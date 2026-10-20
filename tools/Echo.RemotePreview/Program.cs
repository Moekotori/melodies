using Avalonia;
using Avalonia.Headless;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "misc/remote-validation/screenshots");
if (args.Length > 1 && args[1] == "--ui")
{
    try { RemoteChecks.Run(output, args.Length > 2 ? args[2] : null); }
    catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    return;
}
RemoteIndexChecks.Run(Path.GetDirectoryName(output)!);
ProviderChecks.Run(Path.GetDirectoryName(output)!);
RemoteChecks.Run(output);
