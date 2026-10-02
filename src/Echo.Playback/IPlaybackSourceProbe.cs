namespace Echo.Playback;

public interface IPlaybackSourceProbe : IAsyncDisposable
{
    ValueTask<int?> ReadSampleRateAsync(AudioSource source, CancellationToken token);
}
