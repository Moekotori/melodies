using Avalonia;
using Avalonia.Headless;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (args.Contains("--wallpaper-import")) WallpaperImportSmoke.Run();
else if (args.Contains("--background")) BackgroundResidencySmoke.Run();
else if (args.Contains("--queue-search")) { QueueProjectionSmoke.Run(); GlobalSearchConcurrencySmoke.Run(); }
else PresentationResourcesSmoke.Run();
