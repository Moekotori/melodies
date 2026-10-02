namespace Echo.Infrastructure.Radio;

internal static class RadioHttpAddress
{
    public static string Normalize(string value)
    {
        if (value.Length > 2048 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.AbsoluteUri.Length > 2048)
            throw new IOException("电台地址或重定向地址无效，只支持不含用户名和密码的 HTTP / HTTPS 地址。");
        return uri.AbsoluteUri;
    }
}
