namespace Echo.Contracts.Remote;

public sealed record RemoteConnection(string Id, string Name, string BaseUrl, string Username = "", string Password = "",
    string Provider = "webdav", string AuthMode = "password", string UserId = "", string RootPath = "/", OneDriveTokens? OneDrive = null);
public sealed record RemoteSourceInfo(string Id, string Name, string BaseUrl, string Username, string Provider = "webdav");
public sealed record RemoteEntry(string Path, string Name, bool IsDirectory, bool IsAudio, long? SizeBytes, string? Modified,
    string? MediaId = null, string? Title = null, string? Artist = null, string? Album = null, double? DurationSeconds = null, string? Codec = null);
public sealed record RemoteDirectoryPage(IReadOnlyList<RemoteEntry> Entries, int Total, int Page, int PageSize);
public sealed record RemoteMediaSource(string Kind, string Uri, IReadOnlyDictionary<string, string>? Headers = null);
public sealed record OneDriveTokens(string AccessToken, string RefreshToken, string ClientId, long ExpiresUnix);

public static class RemoteProviders
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { "subsonic", "onedrive", "webdav", "smb", "jellyfin", "emby", "sshfs" });
    public static bool IsMounted(string provider) => provider is "smb" or "sshfs";
    public static bool IsMediaServer(string provider) => provider is "jellyfin" or "emby";
}
public interface IOneDriveAuthorization
{
    Task<OneDriveTokens> AuthorizeAsync(string clientId, Func<Uri, CancellationToken, Task> openBrowser, CancellationToken token);
}
public interface IOneDriveTokenWriter
{
    Task<bool> WriteAuthorizationAsync(string id, string expectedRefreshToken, OneDriveTokens next, CancellationToken token);
}

public interface IRemoteConnectionStore
{
    Task<IReadOnlyList<RemoteConnection>> ReadAsync(CancellationToken token);
    Task WriteAsync(IReadOnlyList<RemoteConnection> connections, CancellationToken token);
}
public interface IRemoteFileClient
{
    Task<RemoteDirectoryPage> BrowseAsync(RemoteConnection source, string path, int page, string search, bool audioOnly, CancellationToken token);
    Task<RemoteMediaSource> ResolveMediaAsync(RemoteConnection source, RemoteEntry entry, CancellationToken token);
    async Task<RemoteConnection> TestConnectionAsync(RemoteConnection source, CancellationToken token)
    { await BrowseAsync(source, "/", 0, "", false, token).ConfigureAwait(false); return source; }
    void ForgetSource(string id) { }
}
