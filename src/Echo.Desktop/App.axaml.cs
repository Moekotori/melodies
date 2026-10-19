using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Echo.Core.Client;
using Echo.Desktop.Bootstrap;

namespace Echo.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = OpenControlAsync(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task OpenControlAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        CoreConnection? core = null;
        try
        {
            core = await CoreLauncher.ConnectAsync().ConfigureAwait(false);
            await core.StartAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => Attach(desktop, core));
        }
        catch (Exception)
        {
            if (core is not null) await core.DisposeAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var retry = new Button { Content = "重试", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
                var window = new Window { Title = "ECHO", Width = 420, Height = 180, Content = new StackPanel
                { Margin = new Thickness(24), Spacing = 20, Children = { new TextBlock { Text = "暂时无法连接播放服务，请重试。", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, retry } } };
                desktop.MainWindow = window;
                retry.Click += (_, _) => { retry.IsEnabled = false; _ = OpenControlAsync(desktop); };
                window.Closed += (_, _) => { if (ReferenceEquals(desktop.MainWindow, window)) desktop.Shutdown(); };
                window.Show();
            });
        }
    }

    private static void Attach(IClassicDesktopStyleApplicationLifetime desktop, CoreConnection core)
    {
        var previous = desktop.MainWindow;
        var window = DesktopComposition.CreateWindow(core: core);
        desktop.MainWindow = window;
        previous?.Close();
        var exiting = false;
        var released = false;
        desktop.ShutdownRequested += (_, args) =>
        {
            if (released) return;
            args.Cancel = true; window.RequestExit();
        };
        window.Closed += async (_, _) =>
        {
            if (exiting) return;
            exiting = true;
            try { await core.DisposeAsync(); }
            finally { released = true; desktop.Shutdown(); }
        };
        window.Show();
    }
}
