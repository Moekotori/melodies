namespace Echo.Playback;
public interface IPlaybackWake : IAsyncDisposable
{
    event Action<string?>? DiagnosticChanged;
    void Update(bool required);
}
