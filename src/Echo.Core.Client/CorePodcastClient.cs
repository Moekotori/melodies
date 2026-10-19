using Echo.Contracts.Podcast;
using Echo.Core.Transport;
namespace Echo.Core.Client;

// View cancellation is per RPC; it never calls PodcastLibrary.Cancel on the service owner.
public sealed partial class CorePodcastClient(CoreWireClient wire) : IPodcastLibrary
{
    public void Cancel() { }
    public void Dispose() { }
}
