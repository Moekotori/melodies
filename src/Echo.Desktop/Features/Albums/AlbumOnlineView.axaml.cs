using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumOnlineView : UserControl
{
    public AlbumOnlineView()
    {
        InitializeComponent();
        Resources["AlbumReleaseCardWidth"] = 360d;
        Resources["AlbumFactCardWidth"] = 240d;
        Resources["AlbumReleaseFactWidth"] = 240d;
        SizeChanged += (_, _) => UpdateCardWidths();
        ReleaseFactsGrid.SizeChanged += (_, _) => UpdateCardWidths();
    }

    private void UpdateCardWidths()
    {
        if (Bounds.Width <= 0) return;
        // Keep cards aligned with the reading surface, including the last column gutter.
        var columns = Math.Clamp((int)(Bounds.Width / 240), 1, 4);
        Resources["AlbumFactCardWidth"] = Math.Max(1, Bounds.Width / columns - 12);
        var factSpace = ReleaseFactsGrid.Bounds.Width > 0 ? ReleaseFactsGrid.Bounds.Width : Bounds.Width - 20;
        Resources["AlbumReleaseFactWidth"] = Math.Max(1, factSpace / columns - .5);
        Resources["AlbumReleaseCardWidth"] = Math.Max(1, Bounds.Width / (Bounds.Width >= 760 ? 2 : 1) - 12);
    }

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
