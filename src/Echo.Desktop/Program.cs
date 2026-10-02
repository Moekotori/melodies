using Avalonia;
using Echo.Desktop.Bootstrap;

namespace Echo.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Parse bounded embedded catalogs before creating the UI event loop.
        _ = Localization.LanguageCatalog.Default;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().With(DesktopFonts.CreateOptions());
}
