namespace Echo.Contracts.Settings;

public sealed record BroadcastSnapshot(string Title, string Artist, string Album, string Status,
    double PositionSeconds, double DurationSeconds, double Volume, double PlaybackRate);
public interface ILocalBroadcast
{
    string Address { get; }
    bool IsRunning { get; }
    Task SetEnabledAsync(bool enabled, CancellationToken cancellation);
}
