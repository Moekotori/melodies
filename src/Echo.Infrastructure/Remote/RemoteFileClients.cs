using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

// One window-owned provider router, with shared bounded HTTP resources and no library writes.
public sealed class RemoteFileClients : IRemoteFileClient, IOneDriveAuthorization, IDisposable
{
    private readonly WebDavFileClient webdav;
    private readonly RemoteHttpTransport http;
    private readonly MediaServerFileClient media;
    private readonly SubsonicFileClient subsonic;
    private readonly MountedFileClient mounted = new();
    private readonly OneDriveAuthorization authorization;
    private readonly OneDriveFileClient oneDrive;
    public RemoteFileClients(HttpMessageHandler? jsonHandler = null, HttpMessageHandler? webDavHandler = null)
    {
        webdav = new(webDavHandler); http = new(jsonHandler); media = new(http); subsonic = new(http);
        authorization = new(http); oneDrive = new(http, authorization);
    }
    public void BindTokenWriter(IOneDriveTokenWriter writer) => oneDrive.TokenWriter = writer;
    public Task<RemoteDirectoryPage> BrowseAsync(RemoteConnection source, string path, int page, string search, bool audioOnly, CancellationToken token)
        => Client(source.Provider).BrowseAsync(source, path, page, search, audioOnly, token);
    public Task<RemoteMediaSource> ResolveMediaAsync(RemoteConnection source, RemoteEntry entry, CancellationToken token)
        => Client(source.Provider).ResolveMediaAsync(source, entry, token);
    public Task<RemoteConnection> TestConnectionAsync(RemoteConnection source, CancellationToken token)
        => Client(source.Provider).TestConnectionAsync(source, token);
    public Task<OneDriveTokens> AuthorizeAsync(string clientId, Func<Uri, CancellationToken, Task> openBrowser, CancellationToken token)
        => authorization.AuthorizeAsync(clientId, openBrowser, token);
    public void ForgetSource(string id) => oneDrive.ForgetSource(id);
    private IRemoteFileClient Client(string provider) => provider switch
    {
        "webdav" => webdav, "jellyfin" or "emby" => media, "subsonic" => subsonic,
        "smb" or "sshfs" => mounted, "onedrive" => oneDrive,
        _ => throw new ArgumentException("未知的网盘来源类型。")
    };
    public void Dispose() { authorization.Dispose(); oneDrive.Clear(); webdav.Dispose(); http.Dispose(); }
}
