using Echo.Playback;

// Only the player facade is substituted; the production panel, nodes and subscription run unchanged.
namespace Echo.Desktop.Features.Player;

public sealed class PlayerViewModel(PlaybackZone zone)
{
    public PlaybackZone Zone { get; } = zone;
    public FixtureTrackDetails Details { get; } = new();
    public FixtureText Text { get; } = new();
    public string PerformanceText => "fixture";
    public void RefreshPerformance(PlaybackStateSnapshot frame) { }
}
public sealed class FixtureTrackDetails
{
    public string Format { get; set; } = "FLAC";
    public int SampleRate { get; set; } = 48000;
    public int BitDepth { get; set; } = 24;
}
public sealed class FixtureText
{
    public string this[string key] => key;
    public string Format(string key, params object?[] args) => key + string.Join(',', args);
    public event Action? Changed;
    public void ChangeLanguage() => Changed?.Invoke();
}
