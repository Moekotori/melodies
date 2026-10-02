using System.Text;
using System.Text.Json;
using Echo.Contracts.Media;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

internal static class RemoteBrowseRules
{
    public const int PageSize = 200, MaxEntries = 20000;
    public static void Request(string path, int page, string search)
    {
        if (page is < 0 or > 100 || search.Length > 160) throw new ArgumentException("网盘分页或搜索条件无效。");
        PathParts(path);
    }
    public static string[] PathParts(string path)
    {
        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (path.Length > 4096 || path.Contains('\\') || parts.Any(x => x is "." or ".." || x.Any(char.IsControl)))
            throw new ArgumentException("网盘路径无效。");
        return parts;
    }
    public static bool Audio(string name) => AudioFormatCatalog.DirectAudioExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    public static RemoteDirectoryPage Page(IEnumerable<RemoteEntry> input, int page, string search, bool audioOnly)
    {
        var entries = input.Take(MaxEntries + 1).ToArray();
        if (entries.Length > MaxEntries) throw new IOException("目录超过 20,000 项，请选择较小的音乐目录。");
        var rows = entries.Where(x => (!audioOnly || x.IsDirectory || x.IsAudio) && x.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(rows.Skip(page * PageSize).Take(PageSize).ToArray(), rows.Length, page, PageSize);
    }
    // Every temporary HTTP URL is excluded by the existing checkpoint's header policy.
    public static Dictionary<string, string> SessionHeaders() => new() { ["user-agent"] = "ECHO/Remote" };
    public static Uri Url(string root, string endpoint, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var uri = new Uri(new Uri(root.TrimEnd('/') + "/"), endpoint.TrimStart('/'));
        return parameters is null ? uri : new UriBuilder(uri) { Query = string.Join('&', parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))) }.Uri;
    }
    public static string Text(JsonElement value, string key, int limit = 256)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String
            ? (item.GetString() ?? "")[..Math.Min(item.GetString()?.Length ?? 0, limit)] : "";
    public static long? Number(JsonElement value, string key)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var n) && n >= 0 ? n : null;
    public static IEnumerable<JsonElement> Array(JsonElement value, string key)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Array ? item.EnumerateArray() : [];
    public static string? Date(string value) => DateTimeOffset.TryParse(value, out var date) ? date.ToString("yyyy-MM-dd") : null;
    public static string VirtualPath(string parent, string id, string name, bool directory, string kind = "i")
    {
        if (id.Length is 0 or > 256) throw new IOException("服务返回的条目 ID 无效。");
        var segment = kind + ":" + Convert.ToHexString(Encoding.UTF8.GetBytes(id)) + "~" + name.Replace('/', '∕').Replace('\\', '＼');
        return parent.TrimEnd('/') + "/" + segment + (directory ? "/" : "");
    }
    public static (string Kind, string Id) VirtualId(string path)
    {
        var segment = PathParts(path).LastOrDefault() ?? throw new ArgumentException("请选择音乐目录或歌曲。");
        var separator = segment.IndexOf('~');
        if (separator < 3 || segment[1] != ':') throw new ArgumentException("服务目录路径无效。");
        try { return (segment[..1], Encoding.UTF8.GetString(Convert.FromHexString(segment[2..separator]))); }
        catch (FormatException) { throw new ArgumentException("服务目录路径无效。"); }
    }
}
