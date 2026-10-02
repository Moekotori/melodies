using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Echo.Desktop.Shell;

public partial class TitleBar : UserControl
{
    public TitleBar() => InitializeComponent();
    private Window? Host => TopLevel.GetTopLevel(this) as Window;
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
    private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Avalonia.Visual source) return;
        if (source.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
        if (e.ClickCount == 2) OnMaximize(sender, new RoutedEventArgs());
        else Host?.BeginMoveDrag(e);
    }
}
