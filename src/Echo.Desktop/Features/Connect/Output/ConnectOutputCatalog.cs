namespace Echo.Desktop.Features.Connect.Output;

public enum ConnectDeviceFilter { All, Current, Pinned, Hidden }

internal static class ConnectOutputCatalog
{
    public static IReadOnlyList<ConnectDeviceItem> Visible(IReadOnlyList<ConnectDeviceItem> items, ConnectDeviceFilter filter, string query)
    {
        var text = query.Trim();
        IEnumerable<ConnectDeviceItem> source = filter switch
        {
            ConnectDeviceFilter.Current => items.Where(item => item.IsCurrent),
            ConnectDeviceFilter.Pinned => items.Where(item => item.IsPinned && !item.IsHidden),
            ConnectDeviceFilter.Hidden => items.Where(item => item.IsHidden),
            _ => items.Where(item => !item.IsHidden)
        };
        if (text.Length > 0)
            source = source.Where(item => item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || item.ModeLabel.Contains(text, StringComparison.OrdinalIgnoreCase)
                || item.Detail.Contains(text, StringComparison.OrdinalIgnoreCase));
        return source.OrderByDescending(item => item.IsPinned).ThenByDescending(item => item.IsCurrent)
            .ThenBy(item => item.ModeLabel, StringComparer.Ordinal).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Take(256).ToArray();
    }
}
