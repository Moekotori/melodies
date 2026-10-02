using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public IAlbumLibrary? AlbumLibrary { get; private set; }
    public IArtistLibrary? ArtistLibrary => AlbumLibrary as IArtistLibrary;
    public AlbumArtworkCache? AlbumArtwork { get; private set; }
    public Func<string, IProgress<ScanProgress>, CancellationToken, Task<ScanReport>>? ImportLibrary { get; private set; }
    public void AttachLibrary(IAlbumLibrary library, AlbumArtworkCache artwork,
        Func<string, IProgress<ScanProgress>, CancellationToken, Task<ScanReport>>? import = null)
    { AlbumLibrary = library; AlbumArtwork = artwork; ImportLibrary = import; }
}
