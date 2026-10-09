namespace Echo.Contracts.Remote;

// Stable library identity. Credentials and temporary download URLs never enter this path.
public static class RemoteTrackPaths
{
    public const string Scheme = "remote://";
    public static string Compose(string sourceId, string remotePath)
    {
        if (sourceId.Length is 0 or > 80 || sourceId.AsSpan().ContainsAny('/', '\\') || remotePath.Length is 0 or > 3900 || !remotePath.StartsWith('/'))
            throw new ArgumentException("网盘曲目路径无效。");
        return Scheme + sourceId + remotePath;
    }
    public static bool TrySplit(string? stored, out string sourceId, out string remotePath)
    {
        sourceId = ""; remotePath = "";
        if (stored is null || !stored.StartsWith(Scheme, StringComparison.Ordinal)) return false;
        var rest = stored[Scheme.Length..];
        var slash = rest.IndexOf('/');
        if (slash is <= 0 or > 80) return false;
        sourceId = rest[..slash]; remotePath = rest[slash..];
        return sourceId.Length > 0 && remotePath.StartsWith('/');
    }
    // '/' + 1 sorts after every file under this source and before the next source id.
    public static (string Lower, string Upper) Prefix(string sourceId) => (Scheme + sourceId + "/", Scheme + sourceId + "0");
}
