namespace Echo.Playback;

public interface IPlaybackSourceProbe : IAsyncDisposable
{
    ValueTask<int?> ReadSampleRateAsync(AudioSource source, CancellationToken token);
    ValueTask<double?> ReadDurationAsync(AudioSource source, CancellationToken token);
}
