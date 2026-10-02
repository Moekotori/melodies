namespace Echo.Contracts.Tasks;

public enum PlaybackWorkPressure { Normal, Reduced, Protected }
public enum BackgroundWorkKind { Discovery, Commit, VisibleArtwork }
public enum PlaybackWorkReason { None, PreparingPlayback, TelemetryUnknown, LowBuffer, Underrun, SlowStage, Recovering }
public sealed record BackgroundAdmissionSnapshot(PlaybackWorkPressure Pressure, PlaybackWorkReason Reason,
    int MetadataConcurrency, int CommitBatchLimit, int ActiveMetadata, int WaitingRequests);

public interface IBackgroundWorkAdmission
{
    BackgroundAdmissionSnapshot Snapshot { get; }
    ValueTask<IDisposable> EnterMetadataAsync(CancellationToken cancellation);
    ValueTask WaitAsync(BackgroundWorkKind kind, CancellationToken cancellation);
}
