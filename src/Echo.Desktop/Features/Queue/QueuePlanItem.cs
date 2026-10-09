using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Features.Queue;

public sealed class QueuePlanItem(string itemId, string title, string artist, string reason, string artworkPath, AlbumArtworkCache? artwork, bool first)
{
    public string ItemId => itemId;
    public string Title => title;
    public string Artist => artist;
    public string Reason => reason;
    public string ArtworkPath => artworkPath;
    public AlbumArtworkCache? Artwork => artwork;
    public bool IsFirst => first;
}
