using Avalonia;
using Avalonia.Headless;
using Echo.Desktop;
using Echo.Desktop.Bootstrap;

AppBuilder.Configure<App>().UseSkia().With(DesktopFonts.CreateOptions())
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var output = Path.GetFullPath(args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal)) ?? "misc/ultralight/preview");
if (args.Contains("--titlebar-only")) { TitlebarCustomizationSmoke.Run(output); return; }
if (!args.Contains("--playback-close-only")) UltralightSmoke.Run(output);
PlaybackCloseSmoke.Run(output);
