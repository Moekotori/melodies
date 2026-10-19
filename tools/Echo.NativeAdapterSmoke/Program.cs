using Avalonia;
using Avalonia.Headless;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;

if (args.Length != 1) throw new ArgumentException("Expected the native audio host path.");
AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
NativeHostAdapterSmoke.Run(Path.GetFullPath(args[0]));
