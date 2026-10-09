namespace Echo.Contracts.Radio;

// The monitor reads ICY titles on its own connection and discards the audio bytes.
public interface IRadioMetadataMonitor : IDisposable
{
    event Action? Changed;
    string? StreamUrl { get; }
    string? StreamTitle { get; }
    void Follow(string? url, bool keepTitle);
}
