using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

internal sealed class SubsonicFileClient(RemoteHttpTransport http) : IRemoteFileClient
{
    public async Task<RemoteDirectoryPage> BrowseAsync(RemoteConnection source, string path, int page, string search, bool audioOnly, CancellationToken token)
    {
        RemoteBrowseRules.Request(path, page, search);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(15));
        string endpoint; var parameters = new Dictionary<string, string>();
        var kind = "";
        if (path == "/") endpoint = "getMusicFolders";
        else
        {
            var location = RemoteBrowseRules.VirtualId(path); kind = location.Kind;
            endpoint = kind switch { "f" => "getArtists", "a" => "getArtist", "b" or "d" => "getAlbum", _ => throw new ArgumentException("请选择音乐文件夹、艺术家或专辑。") };
            parameters[kind == "f" ? "musicFolderId" : "id"] = location.Id;
        }
        using var json = await http.JsonAsync(Url(source, endpoint, parameters), budget.Token).ConfigureAwait(false);
        var response = Response(json.RootElement);
        IEnumerable<RemoteEntry> rows;
        if (path == "/") rows = RemoteBrowseRules.Array(Property(response, "musicFolders"), "musicFolder")
            .Select(x => Folder(x, path, "f", "name"));
        else if (kind == "f") rows = RemoteBrowseRules.Array(Property(response, "artists"), "index")
            .SelectMany(x => RemoteBrowseRules.Array(x, "artist")).Select(x => Folder(x, path, "a", "name"));
        else if (kind == "a") rows = RemoteBrowseRules.Array(Property(response, "artist"), "album").Select(x => Folder(x, path, "b", "name"));
        else rows = RemoteBrowseRules.Array(Property(response, "album"), "song").Select(x => Song(x, path));
        return await Task.Run(() => RemoteBrowseRules.Page(rows, page, search, audioOnly), budget.Token).ConfigureAwait(false);
    }
    public Task<RemoteMediaSource> ResolveMediaAsync(RemoteConnection source, RemoteEntry entry, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = entry.MediaId ?? RemoteBrowseRules.VirtualId(entry.Path).Id;
        return Task.FromResult(new RemoteMediaSource("http", Url(source, "stream", new() { ["id"] = id, ["format"] = "raw" }, binary: true).AbsoluteUri,
            RemoteBrowseRules.SessionHeaders()));
    }
    private static Uri Url(RemoteConnection source, string method, Dictionary<string, string> parameters, bool binary = false)
    {
        parameters["v"] = "1.16.1"; parameters["c"] = "ECHO";
        if (!binary) parameters["f"] = "json";
        if (source.AuthMode == "apiKey") parameters["apiKey"] = source.Password;
        else
        {
            var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            parameters["u"] = source.Username; parameters["s"] = salt;
            // MD5 is mandated by the Subsonic token authentication protocol, not used for local storage.
            parameters["t"] = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(source.Password + salt))).ToLowerInvariant();
        }
        return RemoteBrowseRules.Url(source.BaseUrl, "rest/" + method + ".view", parameters);
    }
    private static JsonElement Response(JsonElement json)
    {
        var response = Property(json, "subsonic-response");
        if (RemoteBrowseRules.Text(response, "status") != "ok")
            throw new IOException("Subsonic / Navidrome 返回认证或请求错误，请检查账号与服务设置。");
        return response;
    }
    private static JsonElement Property(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) ? value : default;
    private static string Id(JsonElement item)
    {
        var id = Property(item, "id");
        return id.ValueKind is JsonValueKind.Number ? id.GetRawText() : RemoteBrowseRules.Text(item, "id");
    }
    private static RemoteEntry Folder(JsonElement item, string parent, string kind, string title)
    {
        var id = Id(item); var name = RemoteBrowseRules.Text(item, title);
        if (name.Length == 0) name = RemoteBrowseRules.Text(item, "title");
        return new(RemoteBrowseRules.VirtualPath(parent, id, name, true, kind), name, true, false, null, null, id);
    }
    private static RemoteEntry Song(JsonElement item, string parent)
    {
        var id = Id(item); var title = RemoteBrowseRules.Text(item, "title"); var suffix = RemoteBrowseRules.Text(item, "suffix", 20);
        var name = title + (suffix.Length > 0 ? "." + suffix : "");
        return new(RemoteBrowseRules.VirtualPath(parent, id, name, false, "s"), name, false, true, RemoteBrowseRules.Number(item, "size"),
            RemoteBrowseRules.Date(RemoteBrowseRules.Text(item, "created")), id, title, RemoteBrowseRules.Text(item, "artist"), RemoteBrowseRules.Text(item, "album"),
            RemoteBrowseRules.Number(item, "duration"), suffix.ToUpperInvariant());
    }
}
