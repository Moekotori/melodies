using Avalonia;
using Avalonia.Headless;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (args.Length > 2 && args[2] == "eq-only") { EqIntegrationChecks.Run(Path.GetFullPath(args[0]), Path.GetFullPath(args[1])); return; }
if (args.Length > 2 && args[2] == "spatial-only") { DspSpatialLayoutChecks.Run(Path.GetFullPath(args[0]), Path.GetFullPath(args[1])); return; }
if (args.Length < 3 || args[2] != "resources-only") DspVisualCapture.Run(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));

if (args.Length > 2 && args[2].StartsWith("resources", StringComparison.Ordinal)) Task.Run(() => DspResourceChecks.RunAsync(Path.GetDirectoryName(Path.GetFullPath(args[0]))!)).GetAwaiter().GetResult();
