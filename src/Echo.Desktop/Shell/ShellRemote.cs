using Echo.Application.Remote;
using Echo.Contracts.Remote;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public IRemoteSourceCatalog? RemoteSources { get; private set; }
    public IRemoteFileClient? RemoteFiles { get; private set; }
    public IOneDriveAuthorization? RemoteAuthorization { get; private set; }
    public string OneDriveClientId { get; private set; } = "";
    public IRemoteTrackIndex? RemoteIndex { get; private set; }
    public void AttachRemote(IRemoteSourceCatalog sources, IRemoteFileClient files, IOneDriveAuthorization? authorization = null, string clientId = "")
    { RemoteSources = sources; RemoteFiles = files; RemoteAuthorization = authorization; OneDriveClientId = clientId; }
    public void AttachRemoteIndex(IRemoteTrackIndex index) => RemoteIndex = index;
}
