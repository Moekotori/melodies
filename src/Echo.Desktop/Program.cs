using Avalonia;
using Echo.Desktop.Bootstrap;

namespace Echo.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--complete-restore")
        {
            using var restoreTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            Echo.Infrastructure.Backup.BackupRestore.CompleteAsync(args[1], restoreTimeout.Token).GetAwaiter().GetResult();
        }
        // Parse bounded embedded catalogs before creating the UI event loop.
        _ = Localization.LanguageCatalog.Default;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().With(DesktopFonts.CreateOptions());
}
