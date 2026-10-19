using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Echo.Desktop.Bootstrap;

namespace Echo.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var core = DesktopComposition.CreateCoreRuntime();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = DesktopComposition.CreateWindow(core: core);
            var exiting = false;
            var released = false;
            desktop.ShutdownRequested += (_, args) =>
            {
                if (released) return;
                args.Cancel = true;
                (desktop.MainWindow as Echo.Desktop.Shell.MainWindow)?.RequestExit();
            };
            desktop.MainWindow.Closed += async (_, _) =>
            {
                if (exiting) return;
                exiting = true;
                try { await core.DisposeAsync(); }
                catch (Exception) { /* Core releases audio and storage even if a shutdown step fails. */ }
                finally { released = true; desktop.Shutdown(); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
