using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumOnlineView : UserControl
{
    public AlbumOnlineView() => InitializeComponent();
    private async void OnLink(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (sender is not Button { Tag: string address } || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher || !await launcher.LaunchUriAsync(uri))
                if (DataContext is AlbumOnlineViewModel model) model.Notice = "无法打开浏览器，可稍后重试。";
        }
        catch (Exception) { if (DataContext is AlbumOnlineViewModel model) model.Notice = "无法打开资料链接。"; }
    }
}
