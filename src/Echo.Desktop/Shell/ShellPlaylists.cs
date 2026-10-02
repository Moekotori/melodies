using Echo.Contracts.Library;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public IPlaylistLibrary? PlaylistLibrary => AlbumLibrary as IPlaylistLibrary;
    public string PlaylistSelection { get; set; } = "home";
    public bool PlaylistCards { get; set; }
    public string? LastArchivedPlaylist { get; set; }
}
