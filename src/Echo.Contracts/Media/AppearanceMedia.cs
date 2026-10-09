namespace Echo.Contracts.Media;

public sealed record VideoFrame(int Width, int Height, byte[] Bgra);
public sealed record TrackEnvelope(double Duration, IReadOnlyList<float> Peaks, IReadOnlyList<float> Energy, double GainDb, double TruePeak);
public interface IAppearanceMedia
{
    Task ReadVideoAsync(string path, Func<VideoFrame, CancellationToken, Task> display, CancellationToken cancellation);
    Task<TrackEnvelope> AnalyzeAsync(string path, double duration, CancellationToken cancellation);
}
