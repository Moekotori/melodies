using Avalonia;
using Avalonia.Headless;

AppBuilder.Configure<Application>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (args.Contains("--remaining")) SignalPathPerformanceSmoke.Run(args.Contains("--baseline"));
else PerformanceGapsSmoke.Run(args.Contains("--baseline"));
