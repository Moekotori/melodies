using Echo.Desktop.Features.Player;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public PlayerViewModel Player { get; private set; } = null!;
    public void AttachPlayback(PlayerViewModel player)
    {
        Player = player; player.Queue.Artwork = AlbumArtwork;
        player.Details.AttachLibrary(AlbumLibrary as Echo.Contracts.Library.IPlayerTrackLibrary);
        OnPropertyChanged(nameof(Player));
    }
}
