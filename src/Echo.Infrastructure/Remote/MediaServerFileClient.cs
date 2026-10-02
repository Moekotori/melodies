using System.Net.Http.Headers;
using System.Text.Json;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

internal sealed class MediaServerFileClient(RemoteHttpTransport http) : IRemoteFileClient
{
    public async Task<RemoteDirectoryPage> BrowseAsync(RemoteConnection source, string path, int page, string search, bool audioOnly, CancellationToken token)
    {
        RemoteBrowseRules.Request(path, page, search);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(15));
        var auth = await AuthenticateAsync(source, budget.Token).ConfigureAwait(false);
        var user = auth.UserId.Length > 0 ? "Users/" + Uri.EscapeDataString(auth.UserId) + "/" : "";
        var root = path == "/";
        var query = new Dictionary<string, string> { ["StartIndex"] = (page * 200).ToString(), ["Limit"] = "200", ["SortBy"] = "SortName", ["SortOrder"] = "Ascending",
            ["Fields"] = "MediaSources,DateModified,RunTimeTicks,Artists,Album,Path", ["Recursive"] = "false" };
        if (!root) { query["ParentId"] = RemoteBrowseRules.VirtualId(path).Id; query["IncludeItemTypes"] = "Audio,Folder,CollectionFolder,MusicAlbum,MusicArtist"; }
        if (!root && search.Length > 0) query["SearchTerm"] = search;
        var endpoint = root && auth.UserId.Length > 0 ? user + "Views" : user + "Items";
        using var json = await http.JsonAsync(RemoteBrowseRules.Url(Base(source), endpoint, query), budget.Token, auth.Headers).ConfigureAwait(false);
        var items = RemoteBrowseRules.Array(json.RootElement, "Items").Take(201).ToArray();
        if (items.Length > 200) throw new IOException("服务没有遵守分页上限，请使用较小的音乐目录。");
        if (root)
        {
            var libraries = items.Where(x => RemoteBrowseRules.Text(x, "CollectionType") == "music").Select(x => Entry(x, "/", true));
            return RemoteBrowseRules.Page(libraries, 0, search, audioOnly);
        }
        var rows = items.Select(x => Entry(x, path, false)).Where(x => x.IsDirectory || x.IsAudio).ToArray();
        return new(rows, Count(json.RootElement, rows.Length), page, 200);
    }
    public async Task<RemoteMediaSource> ResolveMediaAsync(RemoteConnection source, RemoteEntry entry, CancellationToken token)
    {
        var auth = await AuthenticateAsync(source, token).ConfigureAwait(false);
        var id = entry.MediaId ?? RemoteBrowseRules.VirtualId(entry.Path).Id;
        var url = RemoteBrowseRules.Url(Base(source), "Audio/" + Uri.EscapeDataString(id) + "/stream",
            new Dictionary<string, string> { ["static"] = "true", ["api_key"] = auth.Token });
        return new("http", url.AbsoluteUri, RemoteBrowseRules.SessionHeaders());
    }
    private async Task<Auth> AuthenticateAsync(RemoteConnection source, CancellationToken token)
    {
        var headers = new Dictionary<string, string> { ["X-Emby-Authorization"] = "MediaBrowser Client=\"ECHO\", Device=\"Desktop\", DeviceId=\"echo-avalonia\", Version=\"1\"" };
        if (source.AuthMode is "token" or "apiKey")
        { headers["X-Emby-Token"] = source.Password; return new(source.Password, source.UserId, headers); }
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        { writer.WriteStartObject(); writer.WriteString("Username", source.Username); writer.WriteString("Pw", source.Password); writer.WriteEndObject(); }
        var content = new ByteArrayContent(bytes.ToArray()); content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var json = await http.JsonAsync(RemoteBrowseRules.Url(Base(source), "Users/AuthenticateByName"), token, headers, content).ConfigureAwait(false);
        var secret = RemoteBrowseRules.Text(json.RootElement, "AccessToken", 16384);
        if (secret.Length == 0 || secret.Any(char.IsControl)) throw new IOException("媒体服务器没有返回有效授权，请检查账号。");
        var user = json.RootElement.TryGetProperty("User", out var info) ? RemoteBrowseRules.Text(info, "Id") : "";
        headers["X-Emby-Token"] = secret; return new(secret, user, headers);
    }
    private static string Base(RemoteConnection source)
    {
        return source.BaseUrl.TrimEnd('/') + "/";
    }
    private static int Count(JsonElement value, int fallback)
    {
        var count = RemoteBrowseRules.Number(value, "TotalRecordCount") ?? fallback;
        return count > 20000 ? throw new IOException("媒体目录超过 20,000 项，请选择较小的音乐目录。") : (int)count;
    }
    private static RemoteEntry Entry(JsonElement item, string parent, bool library)
    {
        var id = RemoteBrowseRules.Text(item, "Id"); var name = RemoteBrowseRules.Text(item, "Name");
        var type = RemoteBrowseRules.Text(item, "Type"); var folder = library || type is "Folder" or "CollectionFolder" or "MusicAlbum" or "MusicArtist";
        var media = RemoteBrowseRules.Array(item, "MediaSources").FirstOrDefault();
        var suffix = RemoteBrowseRules.Text(media, "Container", 20);
        var audio = type == "Audio";
        var filename = audio && suffix.Length > 0 ? name + "." + suffix : name;
        var artist = string.Join(" / ", RemoteBrowseRules.Array(item, "Artists").Where(x => x.ValueKind == JsonValueKind.String).Take(8).Select(x => x.GetString()));
        return new(RemoteBrowseRules.VirtualPath(parent, id, filename, folder), filename, folder, audio,
            RemoteBrowseRules.Number(media, "Size"), RemoteBrowseRules.Date(RemoteBrowseRules.Text(item, "DateModified")), id,
            audio ? name : null, artist.Length > 0 ? artist[..Math.Min(artist.Length, 512)] : null, RemoteBrowseRules.Text(item, "Album"),
            RemoteBrowseRules.Number(item, "RunTimeTicks") / 10000000d, suffix.ToUpperInvariant());
    }
    private sealed record Auth(string Token, string UserId, IReadOnlyDictionary<string, string> Headers);
}
