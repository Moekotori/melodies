using Echo.Contracts.Remote;
namespace Echo.Contracts.Remote;

public interface IRemoteSourceCatalog
{
    IReadOnlyList<RemoteSourceInfo> Sources { get; }
    RemoteConnection? Find(string id);
    RemoteConnection GetConnection(string id);
    Task LoadAsync(CancellationToken token);
    Task SaveAsync(RemoteConnection source, CancellationToken token);
    Task RemoveAsync(string id, CancellationToken token);
    Task<RemoteDirectoryPage> BrowseAsync(string id, string path, int page, string search, bool audioOnly, CancellationToken token);
}
