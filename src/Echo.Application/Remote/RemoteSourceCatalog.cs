using Echo.Contracts.Remote;

namespace Echo.Application.Remote;

// One persisted source owner; browsing never mutates the local library database.
public sealed class RemoteSourceCatalog(IRemoteConnectionStore? store, IRemoteFileClient client) : IRemoteSourceCatalog, IOneDriveTokenWriter
{
    public const int Capacity = 16;
    private readonly SemaphoreSlim lane = new(1, 1);
    private volatile RemoteConnection[] connections = [];
    private bool loaded;
    public IReadOnlyList<RemoteSourceInfo> Sources => connections.Select(x => new RemoteSourceInfo(x.Id, x.Name, x.BaseUrl, x.Username, x.Provider, x.SyncMode ?? "browse")).ToArray();
    public RemoteConnection? Find(string id) => connections.FirstOrDefault(x => x.Id == id);
    public RemoteConnection GetConnection(string id) => connections.FirstOrDefault(x => x.Id == id)
        ?? throw new ArgumentException("这个来源已被移除，请重新选择。");
    public async Task LoadAsync(CancellationToken token)
    {
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (loaded) return;
            var saved = store is null ? [] : await store.ReadAsync(token).ConfigureAwait(false);
            if (saved.Count > Capacity) throw new IOException("网盘来源数量超过上限。");
            var next = saved.Select(Validate).ToArray();
            if (next.Select(x => x.Id).Distinct().Count() != next.Length) throw new IOException("网盘来源 ID 重复。");
            connections = next; loaded = true;
        }
        finally { lane.Release(); }
    }
    public async Task SaveAsync(RemoteConnection source, CancellationToken token)
    {
        source = Validate(source);
        // Confirm the exact draft before changing any persisted configuration.
        source = Validate(await client.TestConnectionAsync(source, token).ConfigureAwait(false));
        await LoadAsync(token).ConfigureAwait(false);
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var next = connections.Where(x => x.Id != source.Id).Append(source).ToArray();
            if (next.Length > Capacity) throw new ArgumentException($"最多添加 {Capacity} 个网盘来源。");
            if (store is not null) await store.WriteAsync(next, token).ConfigureAwait(false);
            connections = next;
            client.ForgetSource(source.Id);
        }
        finally { lane.Release(); }
    }
    public async Task RemoveAsync(string id, CancellationToken token)
    {
        await LoadAsync(token).ConfigureAwait(false);
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var next = connections.Where(x => x.Id != id).ToArray();
            if (store is not null) await store.WriteAsync(next, token).ConfigureAwait(false);
            connections = next;
            client.ForgetSource(id);
        }
        finally { lane.Release(); }
    }
    public Task<RemoteDirectoryPage> BrowseAsync(string id, string path, int page, string search, bool audioOnly, CancellationToken token)
        => client.BrowseAsync(GetConnection(id), path, page, search, audioOnly, token);
    public async Task<bool> WriteAuthorizationAsync(string id, string expectedRefreshToken, OneDriveTokens next, CancellationToken token)
    {
        await lane.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var index = Array.FindIndex(connections, x => x.Id == id && x.Provider == "onedrive" && x.OneDrive?.RefreshToken == expectedRefreshToken);
            if (index < 0) return false;
            var updated = connections.ToArray(); updated[index] = Validate(updated[index] with { OneDrive = next });
            if (store is not null) await store.WriteAsync(updated, token).ConfigureAwait(false);
            connections = updated; return true;
        }
        finally { lane.Release(); }
    }
    public static RemoteConnection Validate(RemoteConnection source)
    {
        if (source.SyncMode is not (null or "" or "index" or "browse")) throw new ArgumentException("同步方式无效。离线镜像尚未提供。");
        source = source with { SyncMode = source.SyncMode == "index" ? "index" : "browse" };
        if (string.IsNullOrWhiteSpace(source.Id) || source.Id.Length > 80 || string.IsNullOrWhiteSpace(source.Name) || source.Name.Length > 100
            || source.Username is null || source.Password is null || source.BaseUrl is null || source.RootPath is null || source.UserId is null
            || source.Username.Length > 256 || source.Password.Length > 16384 || source.Username.Contains(':')
            || source.UserId.Length > 256 || source.RootPath.Length > 4096 || source.BaseUrl.Length > 4096
            || !RemoteProviders.All.Contains(source.Provider) || source.AuthMode is not ("password" or "token" or "apiKey")
            || source.Username.Any(char.IsControl) || source.Password.Any(char.IsControl) || source.UserId.Any(char.IsControl))
            throw new ArgumentException("来源资料无效，请检查名称、地址与认证信息。");
        if (RemoteProviders.IsMounted(source.Provider))
        {
            if (!Path.IsPathFullyQualified(source.BaseUrl)) throw new ArgumentException("请选择系统已经挂载的共享目录。");
            return source with { Name = source.Name.Trim(), BaseUrl = Path.GetFullPath(source.BaseUrl), Username = "", Password = "" };
        }
        if (!Uri.TryCreate(source.BaseUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("请填写 HTTP / HTTPS 服务地址，账号与密码使用独立字段。");
        RemoteEndpointPolicy.Require(uri, source.Username.Length > 0 || source.Password.Length > 0);
        if (source.Provider == "webdav" && source.Password.Length > 4096) throw new ArgumentException("WebDAV 密码过长。");
        if (RemoteProviders.IsMediaServer(source.Provider) && source.AuthMode is "token" or "apiKey" && source.Password.Length == 0)
            throw new ArgumentException("请填写媒体服务器的 API Key 或访问 Token。");
        if (source.Provider == "onedrive" && (uri.AbsoluteUri.TrimEnd('/') != "https://graph.microsoft.com/v1.0" || source.OneDrive is not { } auth
            || !Guid.TryParse(auth.ClientId, out _) || string.IsNullOrEmpty(auth.AccessToken) || string.IsNullOrEmpty(auth.RefreshToken)
            || auth.AccessToken.Length > 16384 || auth.RefreshToken.Length > 16384 || auth.ExpiresUnix < 1
            || auth.AccessToken.Any(char.IsControl) || auth.RefreshToken.Any(char.IsControl)))
            throw new ArgumentException("请先完成 OneDrive 账号授权。");
        return source with { Name = source.Name.Trim(), BaseUrl = uri.AbsoluteUri.TrimEnd('/') + "/" };
    }
}
