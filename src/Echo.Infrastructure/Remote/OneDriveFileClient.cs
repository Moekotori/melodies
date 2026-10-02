using System.Net;
using System.Collections.Concurrent;
using System.Text.Json;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

internal sealed class OneDriveFileClient(RemoteHttpTransport http, OneDriveAuthorization authorization) : IRemoteFileClient
{
    private const string Graph = "https://graph.microsoft.com/v1.0/";
    private readonly SemaphoreSlim refresh = new(1, 1);
    private readonly ConcurrentDictionary<string, (string OriginalRefresh, OneDriveTokens Tokens)> live = new();
    public IOneDriveTokenWriter? TokenWriter { private get; set; }
    public async Task<RemoteConnection> TestConnectionAsync(RemoteConnection source, CancellationToken token)
    {
        using var json = await GraphAsync(source, ItemUrl(source, "/", children: false), token, persist: false).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("folder", out _)) throw new IOException("请选择 OneDrive 文件夹。");
        // Draft refreshes remain transient until the catalog commits this exact validated configuration.
        return source with { OneDrive = await TokenAsync(source, false, token, persist: false).ConfigureAwait(false) };
    }
    public async Task<RemoteDirectoryPage> BrowseAsync(RemoteConnection source, string path, int page, string search, bool audioOnly, CancellationToken token)
    {
        RemoteBrowseRules.Request(path, page, search);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(15));
        var next = ItemUrl(source, path, children: true); var rows = new List<RemoteEntry>(); var seen = new HashSet<string>();
        for (var batch = 0; next is not null && batch < 100; batch++)
        {
            if (!seen.Add(next.AbsoluteUri)) throw new IOException("OneDrive 目录分页循环，请重试。");
            using var json = await GraphAsync(source, next, budget.Token).ConfigureAwait(false);
            foreach (var item in RemoteBrowseRules.Array(json.RootElement, "value"))
            {
                budget.Token.ThrowIfCancellationRequested();
                if (rows.Count >= 20000) throw new IOException("OneDrive 目录超过 20,000 项，请选择较小的音乐目录。");
                var name = RemoteBrowseRules.Text(item, "name", 512);
                if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Any(char.IsControl)) continue;
                var folder = item.TryGetProperty("folder", out _); var audio = !folder && RemoteBrowseRules.Audio(name);
                rows.Add(new(path.TrimEnd('/') + "/" + name + (folder ? "/" : ""), name, folder, audio,
                    RemoteBrowseRules.Number(item, "size"), RemoteBrowseRules.Date(RemoteBrowseRules.Text(item, "lastModifiedDateTime")), RemoteBrowseRules.Text(item, "id")));
            }
            var continuation = RemoteBrowseRules.Text(json.RootElement, "@odata.nextLink", 16384);
            next = continuation.Length == 0 ? null : ValidateGraph(new Uri(continuation));
        }
        if (next is not null) throw new IOException("OneDrive 分页超过安全上限，请选择较小的目录。");
        return await Task.Run(() => RemoteBrowseRules.Page(rows, page, search, audioOnly), budget.Token).ConfigureAwait(false);
    }
    public async Task<RemoteMediaSource> ResolveMediaAsync(RemoteConnection source, RemoteEntry entry, CancellationToken token)
    {
        using var json = await GraphAsync(source, ItemUrl(source, entry.Path, children: false), token).ConfigureAwait(false);
        var url = RemoteBrowseRules.Text(json.RootElement, "@microsoft.graph.downloadUrl", 16384);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != "https" || address.UserInfo.Length > 0 || address.Fragment.Length > 0)
            throw new IOException("OneDrive 没有返回有效的音频地址，请重新授权或检查文件。");
        // Bearer tokens are never forwarded to the signed file host.
        return new("http", address.AbsoluteUri, RemoteBrowseRules.SessionHeaders());
    }
    private async Task<JsonDocument> GraphAsync(RemoteConnection source, Uri url, CancellationToken token, bool persist = true)
    {
        var auth = await TokenAsync(source, false, token, persist).ConfigureAwait(false);
        try { return await http.JsonAsync(ValidateGraph(url), token, new Dictionary<string, string> { ["authorization"] = "Bearer " + auth.AccessToken }).ConfigureAwait(false); }
        catch (RemoteHttpException error) when (error.Status == HttpStatusCode.Unauthorized)
        {
            auth = await TokenAsync(source, true, token, persist).ConfigureAwait(false);
            return await http.JsonAsync(url, token, new Dictionary<string, string> { ["authorization"] = "Bearer " + auth.AccessToken }).ConfigureAwait(false);
        }
    }
    private async Task<OneDriveTokens> TokenAsync(RemoteConnection source, bool force, CancellationToken token, bool persist = true)
    {
        await refresh.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var stored = source.OneDrive ?? throw new IOException("请先完成 OneDrive 账号授权。");
            var current = stored;
            if (live.TryGetValue(source.Id, out var cached) && (cached.OriginalRefresh == stored.RefreshToken || cached.Tokens.RefreshToken == stored.RefreshToken)) current = cached.Tokens;
            if (!force && current.ExpiresUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60) return current;
            var next = await authorization.RefreshAsync(current, token).ConfigureAwait(false);
            if (persist && (TokenWriter is null || !await TokenWriter.WriteAuthorizationAsync(source.Id, current.RefreshToken, next, token).ConfigureAwait(false)))
                throw new IOException("OneDrive 来源已更改或未保存，请重新选择来源或登录。");
            if (live.Count >= 16 && !live.ContainsKey(source.Id)) live.TryRemove(live.Keys.First(), out _);
            live[source.Id] = (stored.RefreshToken, next); return next;
        }
        finally { refresh.Release(); }
    }
    public void ForgetSource(string id) => live.TryRemove(id, out _);
    public void Clear() => live.Clear();
    private static Uri ItemUrl(RemoteConnection source, string path, bool children)
    {
        var parts = RemoteBrowseRules.PathParts(source.RootPath).Concat(RemoteBrowseRules.PathParts(path)).ToArray();
        var endpoint = parts.Length == 0 ? "me/drive/root" : "me/drive/root:/" + string.Join('/', parts.Select(Uri.EscapeDataString)) + (children ? ":" : "");
        if (children) endpoint += "/children?$top=200";
        return new Uri(Graph + endpoint);
    }
    private static Uri ValidateGraph(Uri uri)
    {
        if (uri.Scheme != "https" || uri.Authority != "graph.microsoft.com" || !uri.AbsolutePath.StartsWith("/v1.0/", StringComparison.Ordinal)
            || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) throw new IOException("OneDrive 返回了无效的 Graph 地址。");
        return uri;
    }
}
