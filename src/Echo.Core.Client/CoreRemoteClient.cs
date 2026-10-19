using Echo.Application.Remote;
using Echo.Contracts.Remote;
using Echo.Core.Contracts;
using Echo.Core.Transport;

namespace Echo.Core.Client;

public sealed class CoreRemoteClient(CoreWireClient wire) : IRemoteSourceCatalog, IRemoteFileClient
{
    private RemoteConnection[] connections = [];
    private readonly SemaphoreSlim refresh = new(1, 1);
    public IReadOnlyList<RemoteSourceInfo> Sources => Volatile.Read(ref connections).Select(source =>
        new RemoteSourceInfo(source.Id, source.Name, source.BaseUrl, source.Username, source.Provider, source.SyncMode ?? "browse")).ToArray();
    public RemoteConnection? Find(string id) => Volatile.Read(ref connections).FirstOrDefault(source => source.Id == id);
    public RemoteConnection GetConnection(string id) => Find(id) ?? throw new ArgumentException("这个来源已移除，请刷新。");
    public async Task LoadAsync(CancellationToken token)
    {
        await refresh.WaitAsync(token).ConfigureAwait(false);
        try { Volatile.Write(ref connections, await Call<RemoteConnection[]>(new("connections"), token).ConfigureAwait(false)); }
        finally { refresh.Release(); }
    }
    public async Task SaveAsync(RemoteConnection source, CancellationToken token)
    { await Send(new("save", Draft: new(source)), token).ConfigureAwait(false); await LoadAsync(token).ConfigureAwait(false); }
    public async Task RemoveAsync(string id, CancellationToken token)
    { await Send(new("remove", id), token).ConfigureAwait(false); await LoadAsync(token).ConfigureAwait(false); }
    public Task<RemoteDirectoryPage> BrowseAsync(string id, string path, int page, string search, bool audioOnly, CancellationToken token)
        => Call<RemoteDirectoryPage>(new("browse", id, path, page, search, audioOnly), token);
    public Task<RemoteDirectoryPage> BrowseAsync(RemoteConnection source, string path, int page, string search, bool audioOnly, CancellationToken token)
        => BrowseAsync(source.Id, path, page, search, audioOnly, token);
    public Task<RemoteConnection> TestConnectionAsync(RemoteConnection source, CancellationToken token)
        => Call<RemoteConnection>(new("test", Draft: new(source)), token);
    public Task<RemoteMediaSource> ResolveMediaAsync(RemoteConnection source, RemoteEntry entry, CancellationToken token)
        => Call<RemoteMediaSource>(new("resolve", source.Id, Entry: entry), token);
    public async Task<byte[]?> ReadFileAsync(RemoteConnection source, string path, int maxBytes, CancellationToken token)
        => (await Call<CoreBytes>(new("file", source.Id, path, MaxBytes: maxBytes), token).ConfigureAwait(false)).Bytes;
    public async Task<byte[]?> ReadArtworkAsync(RemoteConnection source, string remotePath, CancellationToken token)
        => (await Call<CoreBytes>(new("artwork", source.Id, remotePath), token).ConfigureAwait(false)).Bytes;
    public Task<string?> ReadServerLyricsAsync(RemoteConnection source, string remotePath, CancellationToken token)
        => Call<string?>(new("lyrics", source.Id, remotePath), token);
    private Task<T> Call<T>(CoreRemoteRequest request, CancellationToken token)
        => wire.CallAsync<T>("remote.call", CoreJson.Value(request), token);
    private Task Send(CoreRemoteRequest request, CancellationToken token)
        => wire.CallAsync("remote.call", CoreJson.Value(request), token);
}
