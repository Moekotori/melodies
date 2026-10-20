using Avalonia.Controls;

namespace Echo.Desktop.Features.Artists;

internal static class ArtistExternalLink
{
    internal static async Task OpenAsync(Control owner, string url, ArtistOverviewViewModel? model)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) return;
        try
        {
            if (TopLevel.GetTopLevel(owner)?.Launcher is { } launcher) await launcher.LaunchUriAsync(uri);
        }
        catch (Exception)
        {
            if (model is not null) model.OnlineNotice = "无法打开链接，请检查系统浏览器设置。";
        }
    }
}
