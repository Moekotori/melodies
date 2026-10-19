using Echo.Application.Podcast;
using Echo.Contracts.Podcast;
using Echo.Playback;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public IPodcastLibrary? Podcasts { get; private set; }
    public PodcastPlayback? PodcastPlayback { get; private set; }

    public void AttachPodcasts(IPodcastLibrary library, IPlaybackSession session)
    {
        Podcasts = library;
        PodcastPlayback = new(session);
    }
}
