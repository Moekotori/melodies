using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Shell;

public partial class TitleBar : UserControl
{
    public TitleBar()
    {
        InitializeComponent();
        if (OperatingSystem.IsMacOS())
        {
            TitlebarSurface.Padding = new Avalonia.Thickness(86, 0, 8, 0);
            WindowButtons.IsVisible = false;
        }
    }
    private Window? Host => TopLevel.GetTopLevel(this) as Window;
    private void OnOpenQuickSettings(object? sender, RoutedEventArgs e)
    {
        MoreButton.Flyout?.Hide();
        MoreButton.Focus();
        if (sender is MenuItem { Tag: string kind } && DataContext is ShellViewModel shell)
            shell.OpenQuickSettingsCommand.Execute(kind);
    }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        MoreButton.Flyout?.Hide();
        base.OnDetachedFromVisualTree(e);
    }
    private void OnMinimize(object? sender, RoutedEventArgs e) { if (Host is { } host) host.WindowState = WindowState.Minimized; }
    private void OnClose(object? sender, RoutedEventArgs e) => Host?.Close();
    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host) host.WindowState = host.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void OnFullscreen(object? sender, RoutedEventArgs e)
    {
        if (Host is { } host) host.WindowState = host.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }
}
