using Echo.Desktop.Features.Player;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Queue;

// The page owns search/selection/history projection; the session remains the only queue owner.
public sealed class QueuePageViewModel : IDisposable
{
    public PlayerViewModel Player { get; }
    public QueueViewModel Queue { get; }
    public QueuePageViewModel(PlayerViewModel player, LocalizationService text, bool history)
    {
        Player = player;
        Queue = new(player.Session, text, presentationActive: false) { Artwork = player.Queue.Artwork, ShowHistory = history };
    }
    public void Dispose() => Queue.Dispose();
}
