using Avalonia;
using Avalonia.Headless;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/artist-detail");
Directory.CreateDirectory(output);
await ArtistProviderSmoke.RunAsync(Path.Combine(output, "providers"));
AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
ArtistDetailSmoke.Run(output);
await Task.Run(() => ArtistCoreSmoke.RunAsync(output)).ConfigureAwait(false);
