using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

// Browse follows the server directory. Indexing uses album pages and is implemented beside this type.
internal sealed partial class SubsonicFileClient(RemoteHttpTransport http) : IRemoteFileClient
{
    public async Task<RemoteConnection> TestConnectionAsync(RemoteConnection source, CancellationToken token)
    {
        using var json = await http.JsonAsync(Url(source, "ping", new()), token).ConfigureAwait(false);
        Response(json.RootElement);
        return source;
    }
    public async Task<RemoteDirectoryPage> BrowseAsync(RemoteConnection source, string path, int page, string search, bool audioOnly, CancellationToken token)
    {
        RemoteBrowseRules.Request(path, page, search);
        var rows = await ListChildrenAsync(source, path, token).ConfigureAwait(false);
        return RemoteBrowseRules.Page(rows, page, search, audioOnly);
    }
    public async Task<IReadOnlyList<RemoteEntry>> ListChildrenAsync(RemoteConnection source, string path, CancellationToken token)
    {
        RemoteBrowseRules.PathParts(path);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(15));
        using var json = await http.JsonAsync(Url(source, Endpoint(path, out var kind, out var id), Parameters(kind, id)), budget.Token).ConfigureAwait(false);
        var response = Response(json.RootElement);
        var rows = kind switch
        {
            "" => Many(Property(response, "musicFolders"), "musicFolder").Select(item => Folder(item, path, "f", "name")),
            "f" => Indexes(response, path),
            "a" => Many(Property(response, "artist"), "album").Select(item => Folder(item, path, "b", "name")),
            "b" => Many(Property(response, "album"), "song").Select(item => Song(item, Property(response, "album"))),
            _ => Many(Property(response, "directory"), "child").Select(item => Child(item, path)),
        };
        return await Task.Run(() =>
        {
            var list = new List<RemoteEntry>();
            foreach (var row in rows)
            {
                budget.Token.ThrowIfCancellationRequested();
                if (row is null) continue;
                if (list.Count >= 25000) throw new IOException("Subsonic 目录超过 25,000 项，请选择较小的音乐目录。");
                list.Add(row);
            }
            return (IReadOnlyList<RemoteEntry>)list;
        }, budget.Token).ConfigureAwait(false);
    }
    public Task<RemoteMediaSource> ResolveMediaAsync(RemoteConnection source, RemoteEntry entry, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = entry.MediaId ?? RemoteBrowseRules.VirtualId(entry.Path).Id;
        return Task.FromResult(new RemoteMediaSource("http", Url(source, "stream", new Dictionary<string, string> { ["id"] = id, ["format"] = "raw", ["maxBitRate"] = "0" }, binary: true).AbsoluteUri,
            RemoteBrowseRules.SessionHeaders()));
    }
    private IEnumerable<RemoteEntry?> Indexes(JsonElement response, string path)
    {
        var indexes = Property(response, "indexes");
        foreach (var entry in Many(indexes, "shortcut").Concat(Many(indexes, "index").SelectMany(index => Many(index, "artist"))))
            yield return Folder(entry, path, "d", "name");
        foreach (var child in Many(indexes, "child")) yield return Child(child, path);
    }
    private RemoteEntry? Child(JsonElement item, string path)
    {
        var id = Id(item);
        if (id.Length == 0) return null;
        var directory = item.TryGetProperty("isDir", out var flag) && (flag.ValueKind == JsonValueKind.True || RemoteBrowseRules.Text(flag, "isDir") == "true" || (flag.ValueKind == JsonValueKind.String && flag.GetString() == "true"));
        if (directory)
        {
            var name = RemoteBrowseRules.Text(item, "title", 512);
            if (name.Length == 0) name = RemoteBrowseRules.Text(item, "name", 512);
            if (name.Length == 0) name = id;
            return new(RemoteBrowseRules.VirtualPath(path, id, name, true, "d"), name, true, false, null, null, id);
        }
        return Song(item, default);
    }
    private RemoteEntry? Song(JsonElement item, JsonElement album)
    {
        var id = Id(item);
        if (id.Length == 0) return null;
        var title = RemoteBrowseRules.Text(item, "title", 512);
        if (title.Length == 0) title = RemoteBrowseRules.Text(item, "name", 512);
        if (title.Length == 0) title = id;
        var suffix = RemoteBrowseRules.Text(item, "suffix", 20);
        var name = suffix.Length == 0 ? title : title + "." + suffix;
        var cover = RemoteBrowseRules.Text(item, "coverArt", 256);
        if (cover.Length == 0) cover = RemoteBrowseRules.Text(album, "coverArt", 256);
        RememberCover(id, cover);
        return new(SongPath(id), name, false, true, RemoteBrowseRules.Number(item, "size"),
            RemoteBrowseRules.Date(RemoteBrowseRules.Text(item, "created")), id, title,
            RemoteBrowseRules.Text(item, "artist", 512), TextOr(item, album, "album", "name"),
            RemoteBrowseRules.Number(item, "duration"), suffix.ToUpperInvariant());
    }
    private static RemoteEntry? Folder(JsonElement item, string parent, string kind, string title)
    {
        var id = Id(item);
        if (id.Length == 0) return null;
        var name = RemoteBrowseRules.Text(item, title, 512);
        if (name.Length == 0) name = RemoteBrowseRules.Text(item, "title", 512);
        if (name.Length == 0) name = id;
        return new(RemoteBrowseRules.VirtualPath(parent, id, name, true, kind), name, true, false, null, null, id);
    }
    private static string Endpoint(string path, out string kind, out string id)
    {
        if (path == "/") { kind = ""; id = ""; return "getMusicFolders"; }
        var location = RemoteBrowseRules.VirtualId(path); kind = location.Kind; id = location.Id;
        return kind switch
        {
            "f" => "getIndexes", "a" => "getArtist", "b" => "getAlbum", "d" => "getMusicDirectory",
            _ => throw new ArgumentException("请选择音乐文件夹或目录。"),
        };
    }
    private static Dictionary<string, string> Parameters(string kind, string id) => kind switch
    {
        "" => [], "f" => new() { ["musicFolderId"] = id }, _ => new() { ["id"] = id },
    };
    internal static string SongPath(string id) => RemoteBrowseRules.VirtualPath("/", id, "song", false, "s");
    internal static Uri Url(RemoteConnection source, string method, Dictionary<string, string> parameters, bool binary = false)
    {
        parameters["v"] = "1.16.1"; parameters["c"] = "ECHO";
        if (!binary) parameters["f"] = "json";
        if (source.AuthMode == "apiKey") parameters["apiKey"] = source.Password;
        else
        {
            var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            parameters["u"] = source.Username; parameters["s"] = salt;
            parameters["t"] = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(source.Password + salt))).ToLowerInvariant();
        }
        return RemoteBrowseRules.Url(source.BaseUrl, "rest/" + method + ".view", parameters);
    }
    internal static JsonElement Response(JsonElement json)
    {
        var response = Property(json, "subsonic-response");
        if (RemoteBrowseRules.Text(response, "status") != "ok")
            throw new IOException("Subsonic / Navidrome 返回认证或请求错误，请检查账号与服务设置。");
        return response;
    }
    internal static JsonElement Property(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) ? value : default;
    internal static IEnumerable<JsonElement> Many(JsonElement parent, string key)
    {
        var item = Property(parent, key);
        if (item.ValueKind == JsonValueKind.Array) { foreach (var entry in item.EnumerateArray()) yield return entry; }
        else if (item.ValueKind == JsonValueKind.Object) yield return item;
    }
    internal static string Id(JsonElement item)
    {
        var id = Property(item, "id");
        var text = id.ValueKind is JsonValueKind.Number ? id.GetRawText() : RemoteBrowseRules.Text(item, "id", 256);
        return text.Length is > 0 and <= 256 ? text : "";
    }
    private static string TextOr(JsonElement song, JsonElement album, string songKey, string albumKey)
    {
        var value = RemoteBrowseRules.Text(song, songKey, 512);
        return value.Length > 0 ? value : RemoteBrowseRules.Text(album, albumKey, 512);
    }
}
