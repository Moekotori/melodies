namespace Echo.Infrastructure.Remote;

internal static class WebDavPaths
{
    public static Uri Resolve(string baseUrl, string path)
    {
        var root = new Uri(baseUrl.TrimEnd('/') + "/");
        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (path.Length > 4096 || path.Contains('\\') || parts.Any(p => p is "." or ".." || p.ContainsAnyControl()))
            throw new ArgumentException("网盘路径无效。");
        return new Uri(root, string.Join('/', parts.Select(Uri.EscapeDataString)) + (path.EndsWith('/') && parts.Length > 0 ? "/" : ""));
    }
    public static string? Relative(Uri root, Uri target)
    {
        if (root.Scheme != target.Scheme || root.Authority != target.Authority || target.Query.Length != 0 || target.Fragment.Length != 0
            || !target.AbsolutePath.StartsWith(root.AbsolutePath, StringComparison.Ordinal)) return null;
        var escaped = target.AbsolutePath[root.AbsolutePath.Length..].TrimEnd('/');
        var parts = escaped.Split('/');
        var decoded = parts.Select(Uri.UnescapeDataString).ToArray();
        if (decoded.Any(p => p is "." or ".." || p.Contains('/') || p.Contains('\\') || p.ContainsAnyControl())) return null;
        return "/" + string.Join('/', decoded) + (target.AbsolutePath.EndsWith('/') && escaped.Length > 0 ? "/" : "");
    }
    private static bool ContainsAnyControl(this string value) => value.Any(char.IsControl);
}
